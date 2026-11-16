using System.Text.Json;
using Medora.Application.Abstractions;
using Medora.Application.Common;
using Medora.Application.Contracts;
using Medora.Domain.Common;
using Medora.Domain.Identity;
using Medora.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Medora.Application.Features.Orders;

/// <summary>
/// Customer order lifecycle. Mirrors the Airbnb booking pipeline:
/// quote (server recomputes) -> idempotent hold (stock reserved 15 min) -> pay -> verification/preparation.
/// </summary>
public sealed class OrderService(IAppDbContext db, IUnitOfWork uow, IStockLedger stock, IPaymentGateway payments, ICurrentUser user, TimeProvider clock)
{
    public const int MaxLines = 20;
    public const int MaxQuantityPerLine = 10;

    public async Task<Result<QuoteDto>> QuoteAsync(QuoteRequest request, CancellationToken ct)
    {
        var fulfilment = Mapping.ParseFulfilment(request.Fulfilment);
        if (fulfilment is null) return Errors.Validation("Choose pickup or delivery.");
        var built = await BuildLinesAsync(request.BranchId, request.Lines, ct);
        if (!built.IsSuccess) return built.Error!;
        var (lines, deliveryFee) = built.Value;
        var q = Pricing.Quote(lines, fulfilment.Value, deliveryFee);
        return new QuoteDto(q.Currency, q.SubtotalMinor, q.DeliveryFeeMinor, q.ServiceFeeMinor, q.RxHandlingFeeMinor, q.TotalMinor, lines.Any(l => l.RequiresPrescription));
    }

    public async Task<Result<OrderDto>> CreateAsync(NewOrderRequest request, string? idempotencyKey, CancellationToken ct)
    {
        var customerId = user.RequireUserId();
        if (idempotencyKey is not { Length: >= 8 and <= 100 }) return Errors.IdempotencyKeyRequired;

        // Idempotency: a retried POST (flaky mobile network) returns the SAME order, never a second one.
        var existing = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.CustomerId == customerId && o.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null) return existing.ToDto();

        var fulfilment = Mapping.ParseFulfilment(request.Fulfilment);
        if (fulfilment is null) return Errors.Validation("Choose pickup or delivery.");
        if (fulfilment == Fulfilment.Delivery && !Guard.IsText(request.DeliveryAddress, 300)) return Errors.Validation("Enter a delivery address.");

        var built = await BuildLinesAsync(request.BranchId, request.Lines, ct);
        if (!built.IsSuccess) return built.Error!;
        var (lines, deliveryFee) = built.Value;

        var now = clock.GetUtcNow();
        string? prescriptionId = null;
        if (lines.Any(l => l.RequiresPrescription))
        {
            if (!Guard.IsId(request.PrescriptionId, "rx")) return Errors.PrescriptionRequired;
            var rx = await db.Prescriptions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PrescriptionId && p.CustomerId == customerId, ct);
            if (rx is null || !rx.IsWithinValidity(DateOnly.FromDateTime(now.UtcDateTime))) return Errors.PrescriptionInvalid;
            prescriptionId = rx.Id;
        }

        var quote = Pricing.Quote(lines, fulfilment.Value, deliveryFee);
        var order = Order.PlaceHold(customerId, request.BranchId, fulfilment.Value, request.DeliveryAddress?.Trim(), idempotencyKey, lines, quote, prescriptionId, now);
        var stockLines = lines.Select(l => new StockLine(l.ProductId, l.Quantity)).ToList();

        try
        {
            var failedProduct = await uow.InTransactionAsync(async token =>
            {
                var failed = await stock.TryReserveAsync(request.BranchId, stockLines, token);
                if (failed is not null) return failed;
                db.Orders.Add(order);
                await db.SaveChangesAsync(token);
                return null;
            }, ct);
            if (failedProduct is not null) return Errors.OutOfStock(lines.First(l => l.ProductId == failedProduct).Name);
        }
        catch (DbUpdateException)
        {
            // Lost a race with a concurrent retry carrying the same key (unique index): return the winner.
            var winner = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.CustomerId == customerId && o.IdempotencyKey == idempotencyKey, ct);
            if (winner is not null) return winner.ToDto();
            throw;
        }
        return order.ToDto();
    }

    public async Task<Result<OrderDto>> PayAsync(string id, PayRequest request, CancellationToken ct)
    {
        var order = await OwnedAsync(id, ct);
        if (order is null) return Errors.NotFound("Order");
        if (order.PaidAt is not null) return order.ToDto(); // idempotent: already paid
        var now = clock.GetUtcNow();
        if (!order.IsHoldActive(now)) return Errors.HoldExpired;
        if (!Guard.IsText(request.PaymentMethodId, 100)) return Errors.Validation("Choose a payment method.");

        // Provider-side idempotency per (order, payment method): a double-tap or network retry with the SAME
        // card can never charge twice, while trying a DIFFERENT card after a decline is a genuinely new attempt.
        var result = await payments.ChargeAsync(order.TotalMinor, order.Currency, request.PaymentMethodId, $"{order.Id}:charge:{request.PaymentMethodId}", ct);
        if (!result.Succeeded) return Errors.PaymentFailed(result.FailureMessage ?? "Your card was declined.");

        order.MarkPaid(result.IntentId!, result.CardBrand, result.CardLast4, now);
        var customer = await db.Users.AsNoTracking().Where(u => u.Id == order.CustomerId).Select(u => new { u.Email, u.FirstName }).FirstAsync(ct);
        db.OutboxMessages.Add(Email(customer.Email, $"Order {ShortId(order.Id)} confirmed",
            order.Status == OrderStatus.AwaitingVerification
                ? $"Hi {customer.FirstName}, we received your order. A pharmacist is checking your prescription; we'll tell you when it's being prepared."
                : $"Hi {customer.FirstName}, your order is being prepared.", now));
        await db.SaveChangesAsync(ct);
        return order.ToDto();
    }

    public async Task<Result<OrderDto>> CancelAsync(string id, CancellationToken ct)
    {
        var order = await OwnedAsync(id, ct);
        if (order is null) return Errors.NotFound("Order");
        if (order.Status == OrderStatus.Cancelled) return order.ToDto();
        if (!RefundPolicy.CustomerCanCancel(order.Status)) return Errors.NotCancellable;

        var now = clock.GetUtcNow();
        var refund = RefundPolicy.RefundFor(order, order.Status);
        if (refund > 0 && order.PaymentIntentId is { } intent)
        {
            // External side effect FIRST, idempotent by key: a crash after it is safely retried.
            order.RecordRefund(await payments.RefundAsync(intent, refund, $"{order.Id}:refund", ct), refund);
        }
        var lines = order.Lines.Select(l => new StockLine(l.ProductId, l.Quantity)).ToList();
        await uow.InTransactionAsync(async token =>
        {
            await stock.ReleaseAsync(order.BranchId, lines, token);
            order.TransitionTo(OrderStatus.Cancelled, now);
            await db.SaveChangesAsync(token);
            return true;
        }, ct);
        return order.ToDto();
    }

    public async Task<Result<OrderDto>> GetAsync(string id, CancellationToken ct)
    {
        if (!Guard.IsId(id, "ord")) return Errors.NotFound("Order");
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return Errors.NotFound("Order");
        var isStaff = user.IsInRole(Roles.Pharmacist) || user.IsInRole(Roles.Admin);
        return order.CustomerId == user.RequireUserId() || isStaff ? order.ToDto() : Errors.NotFound("Order"); // 404, not 403: don't confirm existence
    }

    public async Task<IReadOnlyList<OrderDto>> MineAsync(CancellationToken ct)
    {
        var customerId = user.RequireUserId();
        var orders = await db.Orders.AsNoTracking().Where(o => o.CustomerId == customerId).OrderByDescending(o => o.CreatedAt).Take(100).ToListAsync(ct);
        return orders.Select(o => o.ToDto()).ToList();
    }

    public async Task<Result<PrescriptionDto>> SubmitPrescriptionAsync(NewPrescriptionRequest request, CancellationToken ct)
    {
        if (!Guard.IsText(request.PatientName, 120) || !Guard.IsText(request.PrescriberName, 120)) return Errors.Validation("Enter the patient and prescriber names.");
        if (!Guard.IsText(request.ImageReference, 300)) return Errors.Validation("Attach a photo of the prescription.");
        if (request.Notes is { Length: > 1000 } || request.PrescriberLicense is { Length: > 60 }) return Errors.Validation("Some fields are too long.");
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (request.IssuedOn > today) return Errors.Validation("The issue date can't be in the future.");
        var rx = new Prescription(user.RequireUserId(), request.PatientName.Trim(), request.PrescriberName.Trim(), request.PrescriberLicense?.Trim(),
            request.ImageReference.Trim(), request.Notes?.Trim(), request.IssuedOn, now);
        if (!rx.IsWithinValidity(today)) return Errors.PrescriptionInvalid;
        db.Prescriptions.Add(rx);
        await db.SaveChangesAsync(ct);
        return rx.ToDto(today);
    }

    public async Task<IReadOnlyList<PrescriptionDto>> MyPrescriptionsAsync(CancellationToken ct)
    {
        var customerId = user.RequireUserId();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var list = await db.Prescriptions.AsNoTracking().Where(p => p.CustomerId == customerId).OrderByDescending(p => p.SubmittedAt).Take(50).ToListAsync(ct);
        return list.Select(p => p.ToDto(today)).ToList();
    }

    // ---------------------------------------------------------------------------------------

    private async Task<Order?> OwnedAsync(string id, CancellationToken ct)
    {
        if (!Guard.IsId(id, "ord")) return null;
        var customerId = user.RequireUserId();
        return await db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == customerId, ct);
    }

    /// <summary>Validates and snapshots lines from the CURRENT catalogue (client prices are ignored).</summary>
    private async Task<Result<(List<OrderLine> Lines, long DeliveryFeeMinor)>> BuildLinesAsync(string branchId, IReadOnlyList<OrderLineRequest>? requested, CancellationToken ct)
    {
        if (!Guard.IsId(branchId, "brn")) return Errors.Validation("Choose a pharmacy.");
        if (requested is null || requested.Count is 0 or > MaxLines) return Errors.Validation($"An order needs 1 to {MaxLines} products.");
        if (requested.Any(l => !Guard.IsId(l.ProductId, "prd") || l.Quantity is < 1 or > MaxQuantityPerLine)) return Errors.Validation($"Quantities must be between 1 and {MaxQuantityPerLine}.");

        var merged = requested.GroupBy(l => l.ProductId, StringComparer.Ordinal).Select(g => (ProductId: g.Key, Quantity: g.Sum(l => l.Quantity))).ToList();
        var ids = merged.Select(m => m.ProductId).ToList();

        var branchFee = await db.Branches.AsNoTracking().Where(b => b.Id == branchId).Select(b => (long?)b.DeliveryFeeMinor).FirstOrDefaultAsync(ct);
        if (branchFee is null) return Errors.NotFound("Pharmacy");
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id) && p.IsActive).ToDictionaryAsync(p => p.Id, StringComparer.Ordinal, ct);

        var lines = new List<OrderLine>(merged.Count);
        foreach (var (productId, quantity) in merged)
        {
            if (!products.TryGetValue(productId, out var p)) return Errors.NotFound("Product");
            if (quantity > p.MaxQtyPerOrder) return Errors.QuantityLimit(p.Name, p.MaxQtyPerOrder);
            lines.Add(new OrderLine(p.Id, p.Name, p.GenericName, p.PriceMinor, quantity, p.RequiresPrescription));
        }
        return (lines, branchFee.Value);
    }

    internal static OutboxMessage Email(string to, string subject, string body, DateTimeOffset now) =>
        new("email", JsonSerializer.Serialize(new EmailPayload(to, subject, body)), now);

    internal static string ShortId(string id) => id[^6..].ToUpperInvariant();
}

public sealed record EmailPayload(string To, string Subject, string Body);
