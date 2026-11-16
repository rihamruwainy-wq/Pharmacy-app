namespace Medora.Domain.Common;

/// <summary>Expected business failure (machine code + human message). Exceptions are for bugs.</summary>
public sealed record Error(string Code, string Message);

/// <summary>Result type: expected outcomes are values the caller must handle, not exceptions.</summary>
public readonly struct Result<T>
{
    private readonly T? _value;

    private Result(T? value, Error? error)
    {
        _value = value;
        Error = error;
    }

    public Error? Error { get; }
    public bool IsSuccess => Error is null;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Result failed: {Error!.Code}");

    public static Result<T> Ok(T value) => new(value, null);
    public static Result<T> Fail(Error error) => new(default, error);

    public static implicit operator Result<T>(Error error) => Fail(error);
    public static implicit operator Result<T>(T value) => Ok(value);
}

/// <summary>
/// Money in integer MINOR units (cents). Never floating point. Rounding is half-up (away from zero),
/// identical to the mobile app's Math.round on positives, so a previewed quote equals the charge.
/// </summary>
public readonly record struct Money(long AmountMinor, string Currency)
{
    public static Money Zero(string currency) => new(0, currency);

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.AmountMinor + b.AmountMinor, a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.AmountMinor - b.AmountMinor, a.Currency);
    }

    public Money PercentOf(decimal percent) => new((long)Math.Round(AmountMinor * percent / 100m, MidpointRounding.AwayFromZero), Currency);

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (!string.Equals(a.Currency, b.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Currency mismatch: {a.Currency} vs {b.Currency}");
        }
    }
}

/// <summary>Opaque ids with a type prefix, e.g. <c>ord_0193…</c>: sortable (UUIDv7) and self-describing in logs.</summary>
public static class Ids
{
    public static string New(string prefix) => $"{prefix}_{Guid.CreateVersion7():N}";
}
