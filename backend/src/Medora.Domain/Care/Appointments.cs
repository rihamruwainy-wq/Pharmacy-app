using System.Globalization;
using Medora.Domain.Common;

namespace Medora.Domain.Care;

/// <summary>
/// An in-store clinical service (Airbnb "experience"): vaccinations, blood-pressure checks,
/// consultations. Booked per time slot with a per-slot capacity (number of consultation rooms).
/// </summary>
public sealed class CareService
{
    private CareService() { }

    public CareService(string id, string branchId, string name, string category, string description, int durationMinutes, int capacityPerSlot, long priceMinor, int minimumAge, string imageUrl)
    {
        Id = id; BranchId = branchId; Name = name; Category = category; Description = description; DurationMinutes = durationMinutes;
        CapacityPerSlot = capacityPerSlot; PriceMinor = priceMinor; MinimumAge = minimumAge; ImageUrl = imageUrl;
    }

    public string Id { get; private set; } = default!;
    public string BranchId { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Category { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public int DurationMinutes { get; private set; }
    public int CapacityPerSlot { get; private set; }
    public long PriceMinor { get; private set; }
    public string Currency { get; private set; } = "USD";
    public int MinimumAge { get; private set; }
    public string ImageUrl { get; private set; } = default!;
}

/// <summary>
/// Slot ids are deterministic, e.g. <c>svc_flu~2026-12-03~0930</c> (branch-local wall-clock time):
/// no slot table to maintain, and the same id is generated on every server.
/// Schedule: Monday-Saturday, 09:00-17:00, one slot per service duration.
/// </summary>
public static class SlotSchedule
{
    public const int OpenHour = 9;
    public const int CloseHour = 17;
    /// <summary>Same-day booking needs this much notice (prep + pharmacist availability).</summary>
    public static readonly TimeSpan MinimumNotice = TimeSpan.FromHours(2);
    /// <summary>Free cancellation window (Airbnb "free cancellation until…").</summary>
    public static readonly TimeSpan FreeCancellationBefore = TimeSpan.FromHours(2);

    public static IEnumerable<DateTime> SlotsOn(DateOnly day, int durationMinutes)
    {
        if (day.DayOfWeek == DayOfWeek.Sunday) yield break;
        for (var t = day.ToDateTime(new TimeOnly(OpenHour, 0)); t.AddMinutes(durationMinutes) <= day.ToDateTime(new TimeOnly(CloseHour, 0)); t = t.AddMinutes(durationMinutes))
        {
            yield return t;
        }
    }

    public static string SlotId(string serviceId, DateTime start) =>
        $"{serviceId}~{start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}~{start.ToString("HHmm", CultureInfo.InvariantCulture)}";

    public static bool TryParse(string slotId, out string serviceId, out DateTime start)
    {
        serviceId = string.Empty;
        start = default;
        var parts = slotId.Split('~');
        if (parts.Length != 3) return false;
        if (!DateTime.TryParseExact($"{parts[1]} {parts[2]}", "yyyy-MM-dd HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out start)) return false;
        serviceId = parts[0];
        return true;
    }

    /// <summary>A slot is valid only if it is one the schedule actually generates (no 09:07 slots).</summary>
    public static bool IsScheduled(DateTime start, int durationMinutes) =>
        SlotsOn(DateOnly.FromDateTime(start), durationMinutes).Contains(start);
}

public enum AppointmentStatus
{
    Booked = 0,
    Cancelled = 1,
    Completed = 2,
    NoShow = 3,
}

public sealed class Appointment
{
    private Appointment() { }

    public Appointment(string serviceId, string branchId, string customerId, DateTime slotStart, string slotId, long priceMinor, string? notes, DateTimeOffset now)
    {
        Id = Ids.New("apt");
        ServiceId = serviceId; BranchId = branchId; CustomerId = customerId; SlotStart = slotStart; SlotId = slotId;
        PriceMinor = priceMinor; Notes = notes; Status = AppointmentStatus.Booked; CreatedAt = now;
    }

    public string Id { get; private set; } = default!;
    public string ServiceId { get; private set; } = default!;
    public string BranchId { get; private set; } = default!;
    public string CustomerId { get; private set; } = default!;
    /// <summary>Branch-local wall-clock start (stored as datetime2, no offset).</summary>
    public DateTime SlotStart { get; private set; }
    public string SlotId { get; private set; } = default!;
    public long PriceMinor { get; private set; }
    public string? Notes { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool CanCustomerCancel(DateTime branchNow) => Status == AppointmentStatus.Booked && SlotStart - branchNow >= SlotSchedule.FreeCancellationBefore;

    public void Cancel(DateTimeOffset now)
    {
        if (Status != AppointmentStatus.Booked) throw new InvalidOperationException($"Can't cancel a {Status} appointment.");
        Status = AppointmentStatus.Cancelled;
        CancelledAt = now;
    }

    public void Close(AppointmentStatus outcome)
    {
        if (Status != AppointmentStatus.Booked || outcome is not (AppointmentStatus.Completed or AppointmentStatus.NoShow))
        {
            throw new InvalidOperationException($"Can't move a {Status} appointment to {outcome}.");
        }
        Status = outcome;
    }
}
