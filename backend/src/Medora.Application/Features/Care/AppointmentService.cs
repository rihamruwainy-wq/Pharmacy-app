using Medora.Application.Abstractions;
using Medora.Application.Common;
using Medora.Application.Contracts;
using Medora.Domain.Care;
using Medora.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Medora.Application.Features.Care;

/// <summary>
/// In-store clinical services booked per time slot (Airbnb experiences pipeline): live spots left,
/// capacity re-checked under a lock at booking time, free cancellation until 2 hours before.
/// Branch time is treated as UTC wall-clock in this build (single-time-zone deployment).
/// </summary>
public sealed class AppointmentService(IAppDbContext db, IUnitOfWork uow, ISlotLock slotLock, ICatalogCache cache, ICurrentUser user, TimeProvider clock)
{
    public const int MaxDaysAhead = 30;

    public ValueTask<IReadOnlyList<CareServiceDto>> ServicesAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync<IReadOnlyList<CareServiceDto>>("care:services", async token =>
            (await db.CareServices.AsNoTracking().OrderBy(s => s.Category).ThenBy(s => s.Name).ToListAsync(token)).Select(s => s.ToDto()).ToList(),
            ["care"], TimeSpan.FromHours(1), ct);

    public async Task<Result<IReadOnlyList<SlotDto>>> SlotsAsync(string serviceId, DateOnly from, int days, CancellationToken ct)
    {
        if (!Guard.IsId(serviceId, "svc")) return Errors.NotFound("Service");
        var service = await db.CareServices.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serviceId, ct);
        if (service is null) return Errors.NotFound("Service");

        var branchNow = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(branchNow);
        from = from < today ? today : from;
        days = Math.Clamp(days, 1, 14);
        if (from > today.AddDays(MaxDaysAhead)) return Result<IReadOnlyList<SlotDto>>.Ok([]);

        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = from.AddDays(days).ToDateTime(TimeOnly.MinValue);
        // One grouped query for the whole window (no per-slot round trips).
        var booked = await db.Appointments.AsNoTracking()
            .Where(a => a.ServiceId == serviceId && a.Status == AppointmentStatus.Booked && a.SlotStart >= start && a.SlotStart < end)
            .GroupBy(a => a.SlotStart).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var slots = Enumerable.Range(0, days)
            .SelectMany(d => SlotSchedule.SlotsOn(from.AddDays(d), service.DurationMinutes))
            .Where(s => s - branchNow >= SlotSchedule.MinimumNotice)
            .Select(s => new SlotDto(SlotSchedule.SlotId(serviceId, s), s, service.CapacityPerSlot - booked.GetValueOrDefault(s)))
            .ToList();
        return Result<IReadOnlyList<SlotDto>>.Ok(slots);
    }

    public async Task<Result<AppointmentDto>> BookAsync(NewAppointmentRequest request, CancellationToken ct)
    {
        var customerId = user.RequireUserId();
        if (request.Notes is { Length: > 500 }) return Errors.Validation("Keep notes under 500 characters.");
        if (request.SlotId is not { Length: <= 80 } || !SlotSchedule.TryParse(request.SlotId, out var serviceId, out var startAt)) return Errors.SlotInvalid;
        var service = Guard.IsId(serviceId, "svc") ? await db.CareServices.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serviceId, ct) : null;
        if (service is null) return Errors.SlotInvalid;
        var branchNow = clock.GetUtcNow().UtcDateTime;
        if (!SlotSchedule.IsScheduled(startAt, service.DurationMinutes) || startAt - branchNow < SlotSchedule.MinimumNotice || startAt > branchNow.AddDays(MaxDaysAhead)) return Errors.SlotInvalid;

        var birthDate = await db.Users.AsNoTracking().Where(u => u.Id == customerId).Select(u => u.BirthDate).FirstAsync(ct);
        if (birthDate.AddYears(service.MinimumAge) > DateOnly.FromDateTime(startAt)) return Errors.Validation($"This service is for ages {service.MinimumAge}+.");

        var now = clock.GetUtcNow();
        var appointment = new Appointment(service.Id, service.BranchId, customerId, startAt, SlotSchedule.SlotId(service.Id, startAt), service.PriceMinor, request.Notes?.Trim(), now);
        var outcome = await uow.InTransactionAsync<Error?>(async token =>
        {
            await slotLock.LockServiceAsync(service.Id, token); // capacity check + insert are now serialized per service
            var taken = await db.Appointments.CountAsync(a => a.ServiceId == service.Id && a.SlotStart == startAt && a.Status == AppointmentStatus.Booked, token);
            if (taken >= service.CapacityPerSlot) return Errors.SlotFull;
            if (await db.Appointments.AnyAsync(a => a.CustomerId == customerId && a.SlotStart == startAt && a.Status == AppointmentStatus.Booked, token))
            {
                return Errors.Validation("You already have an appointment at this time.");
            }
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync(token);
            return null;
        }, ct);
        return outcome is null ? ToDto(appointment, service.Name, branchNow) : outcome;
    }

    public async Task<Result<AppointmentDto>> CancelAsync(string id, CancellationToken ct)
    {
        var customerId = user.RequireUserId();
        var appointment = Guard.IsId(id, "apt") ? await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.CustomerId == customerId, ct) : null;
        if (appointment is null) return Errors.NotFound("Appointment");
        var branchNow = clock.GetUtcNow().UtcDateTime;
        if (appointment.Status == AppointmentStatus.Cancelled) return ToDto(appointment, await ServiceNameAsync(appointment.ServiceId, ct), branchNow);
        if (!appointment.CanCustomerCancel(branchNow)) return Errors.TooLateToCancel;
        appointment.Cancel(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return ToDto(appointment, await ServiceNameAsync(appointment.ServiceId, ct), branchNow);
    }

    public async Task<IReadOnlyList<AppointmentDto>> MineAsync(CancellationToken ct)
    {
        var customerId = user.RequireUserId();
        var branchNow = clock.GetUtcNow().UtcDateTime;
        var rows = await db.Appointments.AsNoTracking().Where(a => a.CustomerId == customerId).OrderByDescending(a => a.SlotStart).Take(100)
            .Join(db.CareServices, a => a.ServiceId, s => s.Id, (a, s) => new { a, s.Name }).ToListAsync(ct);
        return rows.Select(r => ToDto(r.a, r.Name, branchNow)).ToList();
    }

    private async Task<string> ServiceNameAsync(string serviceId, CancellationToken ct) =>
        await db.CareServices.AsNoTracking().Where(s => s.Id == serviceId).Select(s => s.Name).FirstAsync(ct);

    private static AppointmentDto ToDto(Appointment a, string serviceName, DateTime branchNow) =>
        new(a.Id, a.ServiceId, serviceName, a.BranchId, a.SlotStart, a.Status.Wire(), a.PriceMinor, a.Notes, a.CanCustomerCancel(branchNow));
}
