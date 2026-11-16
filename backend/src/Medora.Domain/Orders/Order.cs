using Medora.Domain.Common;

namespace Medora.Domain.Orders;

/// <summary>
/// Order lifecycle (Airbnb booking state machine, extended for dispensing):
/// <code>
///   PendingPayment --paid, OTC only--------------> Preparing --pickup---> ReadyForPickup --collected--> Completed
///        |          --paid, contains Rx--> AwaitingVerification --verified--^   \--delivery--> OutForDelivery --delivered--> Completed
///        |                                    \--pharmacist rejects--> Rejected (full refund)
///        |--hold elapsed, unpaid--> Expired
///        \--customer cancels--> Cancelled      (AwaitingVerification / Preparing can also be Cancelled; refund per RefundPolicy)
/// </code>
/// SQL Server enforces the same values with a CHECK constraint.
/// </summary>
public enum OrderStatus
{
    PendingPayment = 0,
    AwaitingVerification = 1,
    Preparing = 2,
    ReadyForPickup = 3,
    OutForDelivery = 4,
    Completed = 5,
    Expired = 6,
    Cancelled = 7,
    Rejected = 8,
}

public enum Fulfilment
{
    Pickup = 0,
    Delivery = 1,
}

public static class OrderStateMachine
{
    /// <summary>How long unpaid stock stays reserved (Airbnb's 15-minute hold).</summary>
    public static readonly TimeSpan HoldWindow = TimeSpan.FromMinutes(15);

    public static bool CanTransition(OrderStatus from, OrderStatus to) => (from, to) switch
    {
        (OrderStatus.PendingPayment, OrderStatus.AwaitingVerification) => true,
        (OrderStatus.PendingPayment, OrderStatus.Preparing) => true,
        (OrderStatus.PendingPayment, OrderStatus.Expired) => true,
        (OrderStatus.PendingPayment, OrderStatus.Cancelled) => true,
        (OrderStatus.AwaitingVerification, OrderStatus.Preparing) => true,
        (OrderStatus.AwaitingVerification, OrderStatus.Rejected) => true,
        (OrderStatus.AwaitingVerification, OrderStatus.Cancelled) => true,
        (OrderStatus.Preparing, OrderStatus.ReadyForPickup) => true,
        (OrderStatus.Preparing, OrderStatus.OutForDelivery) => true,
        (OrderStatus.Preparing, OrderStatus.Cancelled) => true,
        (OrderStatus.ReadyForPickup, OrderStatus.Completed) => true,
        (OrderStatus.OutForDelivery, OrderStatus.Completed) => true,
        _ => false,
    };

    /// <summary>States in which stock is reserved for this order (released on any exit to a terminal state but Completed).</summary>
    public static bool HoldsStock(OrderStatus status) =>
        status is OrderStatus.PendingPayment or OrderStatus.AwaitingVerification or OrderStatus.Preparing
            or OrderStatus.ReadyForPickup or OrderStatus.OutForDelivery;

    public static bool IsTerminal(OrderStatus status) =>
        status is OrderStatus.Completed or OrderStatus.Expired or OrderStatus.Cancelled or OrderStatus.Rejected;
}

/// <summary>Snapshot of a product at purchase time: later price changes never alter a placed order.</summary>
public sealed class OrderLine
{
    private OrderLine() { }

    public OrderLine(string productId, string name, string genericName, long unitPriceMinor, int quantity, bool requiresPrescription)
    {
        ProductId = productId; Name = name; GenericName = genericName; UnitPriceMinor = unitPriceMinor; Quantity = quantity; RequiresPrescription = requiresPrescription;
    }

    public string ProductId { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string GenericName { get; private set; } = default!;
    public long UnitPriceMinor { get; private set; }
    public int Quantity { get; private set; }
    public bool RequiresPrescription { get; private set; }
    public long LineTotalMinor => UnitPriceMinor * Quantity;
}

public sealed class Order
{
    private readonly List<OrderLine> _lines = [];

    private Order() { }

    public static Order PlaceHold(string customerId, string branchId, Fulfilment fulfilment, string? deliveryAddress, string idempotencyKey,
        IReadOnlyList<OrderLine> lines, PriceQuote quote, string? prescriptionId, DateTimeOffset now)
    {
        var order = new Order
        {
            Id = Ids.New("ord"),
            CustomerId = customerId,
            BranchId = branchId,
            Fulfilment = fulfilment,
            DeliveryAddress = fulfilment == Fulfilment.Delivery ? deliveryAddress : null,
            IdempotencyKey = idempotencyKey,
            Currency = quote.Currency,
            SubtotalMinor = quote.SubtotalMinor,
            DeliveryFeeMinor = quote.DeliveryFeeMinor,
            ServiceFeeMinor = quote.ServiceFeeMinor,
            RxHandlingFeeMinor = quote.RxHandlingFeeMinor,
            TotalMinor = quote.TotalMinor,
            PrescriptionId = prescriptionId,
            Status = OrderStatus.PendingPayment,
            HoldExpiresAt = now + OrderStateMachine.HoldWindow,
            CreatedAt = now,
        };
        order._lines.AddRange(lines);
        return order;
    }

    public string Id { get; private set; } = default!;
    public string CustomerId { get; private set; } = default!;
    public string BranchId { get; private set; } = default!;
    public Fulfilment Fulfilment { get; private set; }
    public string? DeliveryAddress { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public IReadOnlyList<OrderLine> Lines => _lines;
    public string Currency { get; private set; } = "USD";
    public long SubtotalMinor { get; private set; }
    public long DeliveryFeeMinor { get; private set; }
    public long ServiceFeeMinor { get; private set; }
    public long RxHandlingFeeMinor { get; private set; }
    public long TotalMinor { get; private set; }
    public string? PrescriptionId { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTimeOffset HoldExpiresAt { get; private set; }

    public string? PaymentIntentId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }
    public string? RefundId { get; private set; }
    public long? RefundMinor { get; private set; }

    public string? VerifiedBy { get; private set; }
    public string? RejectionReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Optimistic concurrency token (SQL rowversion): two staff tapping "ready" can't both win.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public bool ContainsPrescriptionItems => _lines.Any(l => l.RequiresPrescription);
    public bool IsHoldActive(DateTimeOffset now) => Status == OrderStatus.PendingPayment && HoldExpiresAt > now;

    /// <summary>Payment succeeded: Rx orders wait for a pharmacist, OTC orders go straight to preparation.</summary>
    public void MarkPaid(string paymentIntentId, string? cardBrand, string? cardLast4, DateTimeOffset now)
    {
        PaymentIntentId = paymentIntentId;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        PaidAt = now;
        TransitionTo(ContainsPrescriptionItems ? OrderStatus.AwaitingVerification : OrderStatus.Preparing, now);
    }

    public void Verify(string pharmacistId, DateTimeOffset now)
    {
        VerifiedBy = pharmacistId;
        TransitionTo(OrderStatus.Preparing, now);
    }

    public void Reject(string pharmacistId, string reason, DateTimeOffset now)
    {
        VerifiedBy = pharmacistId;
        RejectionReason = reason;
        TransitionTo(OrderStatus.Rejected, now);
    }

    public void RecordRefund(string refundId, long amountMinor)
    {
        RefundId = refundId;
        RefundMinor = amountMinor;
    }

    /// <summary>Same-state transitions are idempotent no-ops; illegal ones throw (a bug, not a business case).</summary>
    public void TransitionTo(OrderStatus to, DateTimeOffset now)
    {
        if (Status == to) return;
        if (!OrderStateMachine.CanTransition(Status, to)) throw new InvalidOperationException($"Illegal transition {Status} -> {to}");
        Status = to;
        if (to == OrderStatus.Completed) CompletedAt = now;
        if (to is OrderStatus.Cancelled or OrderStatus.Rejected) CancelledAt = now;
    }
}

public sealed record PriceQuote(string Currency, long SubtotalMinor, long DeliveryFeeMinor, long ServiceFeeMinor, long RxHandlingFeeMinor)
{
    public long TotalMinor => SubtotalMinor + DeliveryFeeMinor + ServiceFeeMinor + RxHandlingFeeMinor;
}

/// <summary>
/// Server-side pricing (the client's numbers are only a preview; the server ALWAYS recomputes).
/// Mirrors Airbnb's nightly price + cleaning fee + service fee structure.
/// </summary>
public static class Pricing
{
    public const long FreeDeliveryThresholdMinor = 5000;   // $50
    public const decimal ServiceFeePercent = 5m;
    public const long ServiceFeeMinMinor = 50;
    public const long ServiceFeeMaxMinor = 500;
    public const long RxHandlingFeePerLineMinor = 150;     // pharmacist verification + labelling

    public static PriceQuote Quote(IReadOnlyList<OrderLine> lines, Fulfilment fulfilment, long branchDeliveryFeeMinor, string currency = "USD")
    {
        var subtotal = lines.Sum(l => l.LineTotalMinor);
        var delivery = fulfilment == Fulfilment.Delivery && subtotal < FreeDeliveryThresholdMinor ? branchDeliveryFeeMinor : 0;
        var service = Math.Clamp(new Money(subtotal, currency).PercentOf(ServiceFeePercent).AmountMinor, ServiceFeeMinMinor, ServiceFeeMaxMinor);
        var rx = lines.Count(l => l.RequiresPrescription) * RxHandlingFeePerLineMinor;
        return new PriceQuote(currency, subtotal, delivery, service, rx);
    }
}

/// <summary>
/// Refund policy (Airbnb cancellation policy, adapted to dispensing):
/// before payment nothing to refund; paid but not yet prepared = full refund; while preparing the
/// service fee is kept (labour already spent); once ready or out for delivery the customer can no
/// longer cancel (dispensed medicine can't be restocked). Pharmacist rejection is always a full refund.
/// </summary>
public static class RefundPolicy
{
    public static bool CustomerCanCancel(OrderStatus status) =>
        status is OrderStatus.PendingPayment or OrderStatus.AwaitingVerification or OrderStatus.Preparing;

    public static long RefundFor(Order order, OrderStatus cancellingFrom) => cancellingFrom switch
    {
        OrderStatus.PendingPayment => 0,
        OrderStatus.AwaitingVerification => order.TotalMinor,
        OrderStatus.Preparing => order.TotalMinor - order.ServiceFeeMinor,
        _ => 0,
    };
}

/// <summary>
/// A prescription the customer uploads (photo reference + prescriber details). Pharmacists verify it
/// against the order's Rx lines; the order cannot be prepared until then.
/// </summary>
public sealed class Prescription
{
    private Prescription() { }

    public Prescription(string customerId, string patientName, string prescriberName, string? prescriberLicense, string imageReference, string? notes, DateOnly issuedOn, DateTimeOffset now)
    {
        Id = Ids.New("rx");
        CustomerId = customerId; PatientName = patientName; PrescriberName = prescriberName; PrescriberLicense = prescriberLicense;
        ImageReference = imageReference; Notes = notes; IssuedOn = issuedOn; SubmittedAt = now;
    }

    /// <summary>Most jurisdictions treat a prescription older than 6 months as invalid for dispensing.</summary>
    public static readonly TimeSpan Validity = TimeSpan.FromDays(182);

    public string Id { get; private set; } = default!;
    public string CustomerId { get; private set; } = default!;
    public string PatientName { get; private set; } = default!;
    public string PrescriberName { get; private set; } = default!;
    public string? PrescriberLicense { get; private set; }
    /// <summary>Opaque reference to the uploaded image in blob storage (never the bytes in SQL).</summary>
    public string ImageReference { get; private set; } = default!;
    public string? Notes { get; private set; }
    public DateOnly IssuedOn { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }

    public bool IsWithinValidity(DateOnly today) => IssuedOn <= today && today.DayNumber - IssuedOn.DayNumber <= Validity.Days;
}
