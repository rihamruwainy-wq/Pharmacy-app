using Medora.Domain.Common;

namespace Medora.Application.Common;

/// <summary>
/// Machine-readable error codes. The API turns these into RFC 9457 problem+json with a <c>code</c>
/// member; the mobile app maps exactly these codes to messages and UI states.
/// </summary>
public static class Errors
{
    public static Error NotFound(string what) => new("NOT_FOUND", $"{what} not found.");
    public static Error Validation(string message) => new("VALIDATION", message);
    public static readonly Error Forbidden = new("FORBIDDEN", "You can't access this resource.");
    public static Error OutOfStock(string productName) => new("OUT_OF_STOCK", $"{productName} just went out of stock at this branch.");
    public static Error QuantityLimit(string productName, int max) => new("QTY_LIMIT", $"You can order at most {max} of {productName} per order.");
    public static readonly Error PrescriptionRequired = new("RX_REQUIRED", "This order contains prescription-only medicine. Add a valid prescription.");
    public static readonly Error PrescriptionInvalid = new("RX_INVALID", "This prescription is expired or not yours. Upload a current prescription.");
    public static Error PaymentFailed(string message) => new("PAYMENT_FAILED", message);
    public static readonly Error HoldExpired = new("HOLD_EXPIRED", "Your 15-minute reservation expired and the items were released. Place the order again.");
    public static readonly Error IdempotencyKeyRequired = new("VALIDATION", "Send an Idempotency-Key header (8-100 characters) with this request.");
    public static Error IllegalTransition(string from, string to) => new("ILLEGAL_TRANSITION", $"Can't move a {from} order to {to}.");
    public static readonly Error NotCancellable = new("NOT_CANCELLABLE", "This order is already being handed over and can no longer be cancelled.");
    public static readonly Error SlotFull = new("SLOT_FULL", "That time was just booked. Pick another time.");
    public static readonly Error SlotInvalid = new("SLOT_INVALID", "That time isn't available.");
    public static readonly Error TooLateToCancel = new("TOO_LATE_TO_CANCEL", "Appointments can be cancelled free of charge up to 2 hours before the start.");
    public static readonly Error InvalidCredentials = new("INVALID_CREDENTIALS", "Email or password is incorrect.");
    public static readonly Error LockedOut = new("LOCKED_OUT", "Too many attempts. For your security, try again in 15 minutes.");
    public static readonly Error EmailTaken = new("EMAIL_TAKEN", "An account with this email already exists. Log in instead.");
    public static readonly Error WeakPassword = new("WEAK_PASSWORD", "Choose a stronger password (8+ characters, not only letters).");
    public static readonly Error InvalidRefreshToken = new("INVALID_REFRESH_TOKEN", "Your session has ended. Please log in again.");
    public static readonly Error AiUnavailable = new("AI_UNAVAILABLE", "The assistant is not configured on this server.");
}

/// <summary>Small input guards shared by use cases (the API boundary is untrusted).</summary>
public static class Guard
{
    public static bool IsEmail(string? value) =>
        value is { Length: > 3 and <= 254 } && value.IndexOf('@', StringComparison.Ordinal) is > 0 and var at && at < value.Length - 3 && value.IndexOf('.', at) > at + 1 && !value.Contains(' ', StringComparison.Ordinal);

    public static bool IsId(string? value, string prefix) =>
        value is { Length: > 4 and <= 64 } && value.StartsWith(prefix + "_", StringComparison.Ordinal) && value.Skip(prefix.Length + 1).All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    public static bool IsName(string? value) => value is { Length: >= 1 and <= 60 } && !string.IsNullOrWhiteSpace(value);

    public static bool IsText(string? value, int max) => value is not null && value.Length <= max && !string.IsNullOrWhiteSpace(value);

    /// <summary>Escapes LIKE wildcards so a search for "100%" doesn't match everything.</summary>
    public static string LikeContains(string term) =>
        "%" + term.Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal).Replace("_", "[_]", StringComparison.Ordinal) + "%";
}
