using Medora.Domain.Care;
using Medora.Domain.Catalog;
using Medora.Domain.Identity;
using Medora.Domain.Knowledge;
using Medora.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Medora.Application.Abstractions;

/// <summary>
/// Persistence port. Exposes DbSets (provider-neutral EF Core) so use cases write PROJECTING,
/// no-tracking LINQ (only the columns a screen needs). SQL Server specifics live in Infrastructure.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Favorite> Favorites { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<Branch> Branches { get; }
    DbSet<Product> Products { get; }
    DbSet<InventoryItem> Inventory { get; }
    DbSet<Order> Orders { get; }
    DbSet<Prescription> Prescriptions { get; }
    DbSet<CareService> CareServices { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<MonographSection> MonographSections { get; }
    DbSet<DrugInteraction> DrugInteractions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Runs work in ONE database transaction, wrapped in the provider's retry strategy (transient faults replay the whole unit).</summary>
public interface IUnitOfWork
{
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}

public sealed record StockLine(string ProductId, int Quantity);

/// <summary>
/// Stock ledger. Every method must run inside <see cref="IUnitOfWork.InTransactionAsync{T}"/>.
/// Reservation is a conditional set-based UPDATE per line (<c>WHERE OnHand - Reserved &gt;= qty</c>):
/// the database arbitrates races, no application lock and no lost updates.
/// </summary>
public interface IStockLedger
{
    /// <summary>All-or-nothing: returns the first product that could not be reserved, or null on success.</summary>
    Task<string?> TryReserveAsync(string branchId, IReadOnlyList<StockLine> lines, CancellationToken cancellationToken);
    Task ReleaseAsync(string branchId, IReadOnlyList<StockLine> lines, CancellationToken cancellationToken);
    /// <summary>Dispensed: the reserved units leave the shelf (OnHand and Reserved both decrease).</summary>
    Task ConsumeAsync(string branchId, IReadOnlyList<StockLine> lines, CancellationToken cancellationToken);
}

/// <summary>Serializes capacity checks for one bookable service (UPDLOCK on its row inside the current transaction).</summary>
public interface ISlotLock
{
    Task LockServiceAsync(string serviceId, CancellationToken cancellationToken);
}

/// <summary>Who is calling (from the validated bearer token).</summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? BranchId { get; }
    bool IsInRole(string role);
    string RequireUserId() => UserId ?? throw new UnauthorizedAccessException();
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password, out bool needsRehash);
    /// <summary>Burns the same CPU as a real verify (for unknown users: no timing oracle).</summary>
    void VerifyDummy(string password);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface IAccessTokenIssuer
{
    AccessToken Issue(User user);
}

/// <summary>Two-level cache (in-process L1 + optional distributed L2) with tag invalidation and stampede protection.</summary>
public interface ICatalogCache
{
    ValueTask<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, string[] tags, TimeSpan ttl, CancellationToken cancellationToken);
    ValueTask InvalidateTagAsync(string tag, CancellationToken cancellationToken);
}

public sealed record PaymentResult(bool Succeeded, string? IntentId, string? CardBrand, string? CardLast4, string? FailureCode, string? FailureMessage);

/// <summary>
/// Payment provider port (Stripe in production). The mobile app tokenizes the card with the
/// provider's SDK and sends only a payment-method id - card numbers never touch this API.
/// </summary>
public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(long amountMinor, string currency, string paymentMethodId, string idempotencyKey, CancellationToken cancellationToken);
    Task<string> RefundAsync(string intentId, long amountMinor, string idempotencyKey, CancellationToken cancellationToken);
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}
