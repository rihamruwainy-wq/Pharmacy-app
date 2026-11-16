using Medora.Application.Abstractions;
using Medora.Application.Common;
using Medora.Application.Contracts;
using Medora.Application.Features.Orders;
using Medora.Domain.Common;
using Medora.Domain.Identity;
using Medora.Domain.Knowledge;
using Medora.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Medora.Application.Features.Pharmacist;

/// <summary>
/// Pharmacist workflow: verification queue (with DETERMINISTIC interaction screening), approve/reject
/// prescriptions, and advance orders through preparation -> handover -> completed (dispensed).
/// Pharmacists only see their own branch; admins see every branch.
/// </summary>
public sealed class PharmacistService(IAppDbContext db, IUnitOfWork uow, IStockLedger stock, IPaymentGateway payments, ICurrentUser user, TimeProvider clock)
{
    /// <summary>How far back a patient's dispensed medicines count as "current" for interaction screening.</summary>
    public static readonly TimeSpan MedicationHistoryWindow = TimeSpan.FromDays(180);

    public async Task<IReadOnlyList<VerificationItemDto>> QueueAsync(CancellationToken ct)
    {
        var orders = await ScopedOrders().AsNoTracking()
            .Where(o => o.Status == OrderStatus.AwaitingVerification)
            .OrderBy(o => o.PaidAt).Take(50).ToListAsync(ct);
        var items = new List<VerificationItemDto>(orders.Count);
        foreach (var order in orders) items.Add(await BuildItemAsync(order, ct));
        return items;
    }

    public async Task<Result<VerificationItemDto>> DetailAsync(string orderId, CancellationToken ct)
    {
        if (!Guard.IsId(orderId, "ord")) return Errors.NotFound("Order");
        var order = await ScopedOrders().AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        return order is null ? Errors.NotFound("Order") : await BuildItemAsync(order, ct);
    }

    /// <summary>Orders being worked on at the branch (preparing / ready / out for delivery), oldest first.</summary>
    public async Task<IReadOnlyList<OrderDto>> ActiveAsync(CancellationToken ct)
    {
        var orders = await ScopedOrders().AsNoTracking()
            .Where(o => o.Status == OrderStatus.Preparing || o.Status == OrderStatus.ReadyForPickup || o.Status == OrderStatus.OutForDelivery)
            .OrderBy(o => o.PaidAt).Take(100).ToListAsync(ct);
        return orders.Select(o => o.ToDto()).ToList();
    }

    public async Task<Result<OrderDto>> VerifyAsync(string orderId, VerifyRequest request, CancellationToken ct)
    {
        var pharmacistId = user.RequireUserId();
        var order = Guard.IsId(orderId, "ord") ? await ScopedOrders().FirstOrDefaultAsync(o => o.Id == orderId, ct) : null;
        if (order is null) return Errors.NotFound("Order");
        if (order.Status != OrderStatus.AwaitingVerification) return Errors.IllegalTransition(order.Status.Wire(), request.Approve ? "preparing" : "rejected");
        var now = clock.GetUtcNow();

        if (request.Approve)
        {
            var rx = order.PrescriptionId is null ? null : await db.Prescriptions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == order.PrescriptionId, ct);
            if (rx is null || !rx.IsWithinValidity(DateOnly.FromDateTime(now.UtcDateTime))) return Errors.PrescriptionInvalid;
            order.Verify(pharmacistId, now);
            db.AuditEntries.Add(new AuditEntry(pharmacistId, "rx.verified", order.Id, $"prescription={rx.Id}", now));
            await NotifyAsync(order, $"Order {OrderService.ShortId(order.Id)}: prescription approved", "A pharmacist approved your prescription. Your order is being prepared.", now, ct);
            await db.SaveChangesAsync(ct);
            return order.ToDto();
        }

        if (!Guard.IsText(request.Reason, 500)) return Errors.Validation("Give the customer a reason for rejecting the prescription.");
        // Rejection: full refund + stock released + customer told why.
        if (order.PaymentIntentId is { } intent) order.RecordRefund(await payments.RefundAsync(intent, order.TotalMinor, $"{order.Id}:refund", ct), order.TotalMinor);
        var lines = order.Lines.Select(l => new StockLine(l.ProductId, l.Quantity)).ToList();
        await uow.InTransactionAsync(async token =>
        {
            await stock.ReleaseAsync(order.BranchId, lines, token);
            order.Reject(pharmacistId, request.Reason!.Trim(), now);
            db.AuditEntries.Add(new AuditEntry(pharmacistId, "rx.rejected", order.Id, order.RejectionReason, now));
            await NotifyAsync(order, $"Order {OrderService.ShortId(order.Id)}: prescription not accepted",
                $"A pharmacist couldn't accept your prescription: {order.RejectionReason} You have been refunded in full.", now, token);
            await db.SaveChangesAsync(token);
            return true;
        }, ct);
        return order.ToDto();
    }

    public async Task<Result<OrderDto>> AdvanceAsync(string orderId, AdvanceRequest request, CancellationToken ct)
    {
        var staffId = user.RequireUserId();
        var order = Guard.IsId(orderId, "ord") ? await ScopedOrders().FirstOrDefaultAsync(o => o.Id == orderId, ct) : null;
        if (order is null) return Errors.NotFound("Order");
        var to = Mapping.ParseStatus(request.To);
        if (to is null) return Errors.Validation("Unknown status.");

        // Handover must match how the customer chose to receive the order.
        var allowed = to switch
        {
            OrderStatus.ReadyForPickup => order.Status == OrderStatus.Preparing && order.Fulfilment == Fulfilment.Pickup,
            OrderStatus.OutForDelivery => order.Status == OrderStatus.Preparing && order.Fulfilment == Fulfilment.Delivery,
            OrderStatus.Completed => order.Status is OrderStatus.ReadyForPickup or OrderStatus.OutForDelivery,
            _ => false,
        };
        if (!allowed) return Errors.IllegalTransition(order.Status.Wire(), to.Value.Wire());

        var now = clock.GetUtcNow();
        await uow.InTransactionAsync(async token =>
        {
            if (to == OrderStatus.Completed)
            {
                // Dispensed: reserved units leave the shelf.
                await stock.ConsumeAsync(order.BranchId, order.Lines.Select(l => new StockLine(l.ProductId, l.Quantity)).ToList(), token);
                db.AuditEntries.Add(new AuditEntry(staffId, "order.dispensed", order.Id, string.Join(", ", order.Lines.Select(l => $"{l.Quantity}x {l.ProductId}")), now));
            }
            order.TransitionTo(to.Value, now);
            if (to == OrderStatus.ReadyForPickup) await NotifyAsync(order, $"Order {OrderService.ShortId(order.Id)} is ready", "Your order is ready to collect. Bring a photo ID for prescription medicines.", now, token);
            if (to == OrderStatus.OutForDelivery) await NotifyAsync(order, $"Order {OrderService.ShortId(order.Id)} is on its way", "Your order is out for delivery.", now, token);
            await db.SaveChangesAsync(token);
            return true;
        }, ct);
        return order.ToDto();
    }

    // ---------------------------------------------------------------------------------------

    private IQueryable<Order> ScopedOrders()
    {
        if (user.IsInRole(Roles.Admin)) return db.Orders;
        var branchId = user.BranchId;
        return branchId is null ? db.Orders.Where(_ => false) : db.Orders.Where(o => o.BranchId == branchId);
    }

    private async Task<VerificationItemDto> BuildItemAsync(Order order, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var customer = await db.Users.AsNoTracking().Where(u => u.Id == order.CustomerId)
            .Select(u => new { u.FirstName, u.LastName, u.Allergies, u.BirthDate }).FirstAsync(ct);
        var rx = order.PrescriptionId is null ? null : await db.Prescriptions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == order.PrescriptionId, ct);
        var recent = await RecentMedicinesAsync(order.CustomerId, order.Id, now, ct);
        var orderDrugs = order.Lines.Select(l => l.GenericName).ToList();
        var interactions = await ScreenAsync(orderDrugs, recent, ct);
        // Allergy alerts first: they are the most actionable (and are always "contraindicated" until the pharmacist decides).
        var alerts = AllergyScreen.Find(customer.Allergies, orderDrugs)
            .Select(a => new InteractionAlertDto($"{a.DrugClass} allergy", a.GenericName, "allergy",
                $"Patient declared \"{a.DeclaredAllergy}\"; {a.GenericName} belongs to the {a.DrugClass}.",
                "Do not dispense until the allergy history is clarified with the patient and the prescriber; consider an alternative."))
            .Concat(interactions.Select(i => i.ToDto()))
            .ToList();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var age = today.Year - customer.BirthDate.Year - (today < customer.BirthDate.AddYears(today.Year - customer.BirthDate.Year) ? 1 : 0);
        return new VerificationItemDto(order.ToDto(), $"{customer.FirstName} {customer.LastName}", customer.Allergies, age, rx?.ToDto(today), alerts, recent);
    }

    /// <summary>Generic names dispensed to this patient recently (other orders), for cross-order interaction screening.</summary>
    internal async Task<IReadOnlyList<string>> RecentMedicinesAsync(string customerId, string excludeOrderId, DateTimeOffset now, CancellationToken ct)
    {
        var since = now - MedicationHistoryWindow;
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.Id != excludeOrderId && o.PaidAt != null && o.PaidAt >= since
                        && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected && o.Status != OrderStatus.Expired)
            .ToListAsync(ct);
        return orders.SelectMany(o => o.Lines).Select(l => l.GenericName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    internal async Task<IReadOnlyList<DrugInteraction>> ScreenAsync(IReadOnlyList<string> orderDrugs, IReadOnlyList<string> currentMedicines, CancellationToken ct)
    {
        var names = orderDrugs.Concat(currentMedicines).Select(n => n.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count < 2) return [];
        var candidates = await db.DrugInteractions.AsNoTracking().Where(i => names.Contains(i.DrugA) && names.Contains(i.DrugB)).ToListAsync(ct);
        return InteractionScreen.Find(orderDrugs, currentMedicines, candidates);
    }

    private async Task NotifyAsync(Order order, string subject, string body, DateTimeOffset now, CancellationToken ct)
    {
        var email = await db.Users.AsNoTracking().Where(u => u.Id == order.CustomerId).Select(u => u.Email).FirstAsync(ct);
        db.OutboxMessages.Add(OrderService.Email(email, subject, body, now));
    }
}
