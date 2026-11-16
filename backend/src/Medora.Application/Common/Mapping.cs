using System.Linq.Expressions;
using Medora.Application.Contracts;
using Medora.Domain.Care;
using Medora.Domain.Catalog;
using Medora.Domain.Identity;
using Medora.Domain.Knowledge;
using Medora.Domain.Orders;

namespace Medora.Application.Common;

/// <summary>Entity -> DTO. Expression projections are translated to SQL (only the needed columns are read).</summary>
public static class Mapping
{
    public static UserDto ToDto(this User u) => new(u.Id, u.FirstName, u.LastName, u.Email, u.Roles, u.BranchId, u.Allergies);

    public static readonly Expression<Func<Product, ProductSummaryDto>> ProductSummary = p =>
        new ProductSummaryDto(p.Id, p.Name, p.Brand, p.Category, p.Form, p.Strength, p.PriceMinor, p.Currency, p.RequiresPrescription, p.ImageUrl, p.Rating, p.ReviewCount);

    public static ProductDto ToDto(this Product p) =>
        new(p.Id, p.Name, p.GenericName, p.Brand, p.Category, p.Form, p.Strength, p.PriceMinor, p.Currency, p.RequiresPrescription, p.MaxQtyPerOrder,
            p.ImageUrl, p.Description, p.ActiveIngredients, p.Rating, p.ReviewCount);

    public static BranchDto ToDto(this Branch b) => new(b.Id, b.Name, b.City, b.Address, b.Latitude, b.Longitude, b.OpeningHours, b.DeliveryFeeMinor, b.Open24h);

    public static string Wire(this OrderStatus s) => s switch
    {
        OrderStatus.PendingPayment => "pending_payment",
        OrderStatus.AwaitingVerification => "awaiting_verification",
        OrderStatus.Preparing => "preparing",
        OrderStatus.ReadyForPickup => "ready_for_pickup",
        OrderStatus.OutForDelivery => "out_for_delivery",
        OrderStatus.Completed => "completed",
        OrderStatus.Expired => "expired",
        OrderStatus.Cancelled => "cancelled",
        OrderStatus.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    public static OrderStatus? ParseStatus(string? wire) =>
        Enum.GetValues<OrderStatus>().Cast<OrderStatus?>().FirstOrDefault(s => s!.Value.Wire() == wire);

    public static string Wire(this Fulfilment f) => f == Fulfilment.Delivery ? "delivery" : "pickup";

    public static Fulfilment? ParseFulfilment(string? wire) => wire switch
    {
        "pickup" => Fulfilment.Pickup,
        "delivery" => Fulfilment.Delivery,
        _ => null,
    };

    public static OrderDto ToDto(this Order o)
    {
        var canCancel = RefundPolicy.CustomerCanCancel(o.Status);
        return new OrderDto(o.Id, o.BranchId, o.Fulfilment.Wire(), o.DeliveryAddress, o.Status.Wire(),
            o.Lines.Select(l => new OrderLineDto(l.ProductId, l.Name, l.UnitPriceMinor, l.Quantity, l.RequiresPrescription, l.LineTotalMinor)).ToList(),
            o.Currency, o.SubtotalMinor, o.DeliveryFeeMinor, o.ServiceFeeMinor, o.RxHandlingFeeMinor, o.TotalMinor,
            o.HoldExpiresAt, o.CreatedAt, o.PaidAt, o.CardBrand, o.CardLast4, o.RefundMinor, o.RejectionReason, o.PrescriptionId,
            canCancel, canCancel ? RefundPolicy.RefundFor(o, o.Status) : 0);
    }

    public static PrescriptionDto ToDto(this Prescription p, DateOnly today) =>
        new(p.Id, p.PatientName, p.PrescriberName, p.PrescriberLicense, p.ImageReference, p.Notes, p.IssuedOn, p.SubmittedAt, p.IsWithinValidity(today));

    public static InteractionAlertDto ToDto(this DrugInteraction i) =>
        new(i.DrugA, i.DrugB, i.Severity.ToString().ToLowerInvariant(), i.Effect, i.Management);

    public static CareServiceDto ToDto(this CareService s) =>
        new(s.Id, s.BranchId, s.Name, s.Category, s.Description, s.DurationMinutes, s.PriceMinor, s.Currency, s.MinimumAge, s.ImageUrl);

    public static string Wire(this AppointmentStatus s) => s switch
    {
        AppointmentStatus.Booked => "booked",
        AppointmentStatus.Cancelled => "cancelled",
        AppointmentStatus.Completed => "completed",
        AppointmentStatus.NoShow => "no_show",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };
}
