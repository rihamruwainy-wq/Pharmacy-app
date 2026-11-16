using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Medora.Application.Abstractions;
using Medora.Application.Features.Ai;
using Medora.Application.Features.Orders;
using Medora.Domain.Identity;
using Medora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Medora.Domain.Orders;

namespace Medora.Infrastructure;

// ================================================================== Identity

/// <summary>ASP.NET Core Identity's hasher: PBKDF2-HMAC-SHA512, per-user salt, versioned format (transparent rehash on login).</summary>
internal sealed class AspNetPasswordHasher : IPasswordHasher
{
    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly object Nobody = new();
    private static readonly string DummyHash = Hasher.HashPassword(Nobody, Guid.NewGuid().ToString());

    public string Hash(string password) => Hasher.HashPassword(Nobody, password);

    public bool Verify(string hash, string password, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrEmpty(hash)) return false;
        var result = Hasher.VerifyHashedPassword(Nobody, hash, password);
        needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
        return result != PasswordVerificationResult.Failed;
    }

    public void VerifyDummy(string password) => Hasher.VerifyHashedPassword(Nobody, DummyHash, password);
}

public sealed class JwtSettings
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "medora-api";
    public string Audience { get; set; } = "medora-app";
    /// <summary>At least 32 bytes. From user-secrets / Key Vault / env in production, never source control.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public SymmetricSecurityKey Key()
    {
        var bytes = Encoding.UTF8.GetBytes(SigningKey);
        if (bytes.Length < 32) throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");
        return new SymmetricSecurityKey(bytes);
    }
}

/// <summary>Short-lived HS256 access tokens carrying role(s) and, for staff, the branch they work at.</summary>
internal sealed class JwtAccessTokenIssuer(IOptions<JwtSettings> options, TimeProvider clock) : IAccessTokenIssuer
{
    private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };
    private readonly JwtSettings _settings = options.Value;
    private readonly SigningCredentials _credentials = new(options.Value.Key(), SecurityAlgorithms.HmacSha256);

    public AccessToken Issue(User user)
    {
        var now = clock.GetUtcNow();
        var expires = now + _settings.AccessTokenLifetime;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.GivenName, user.FirstName),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString("N")),
        };
        claims.AddRange(user.Roles.Select(r => new Claim("role", r)));
        if (user.BranchId is not null) claims.Add(new Claim("branch", user.BranchId));

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = _credentials,
        });
        return new AccessToken(token, expires);
    }
}

// ================================================================== Payments

/// <summary>
/// Development payment gateway honouring Stripe's published TEST payment-method ids, so the mobile
/// checkout behaves like Stripe test mode without an account. Idempotent by key, like Stripe.
/// Production: replace with a Stripe adapter (PaymentIntents + PaymentSheet on the device + signed webhooks).
/// </summary>
internal sealed class MockPaymentGateway : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, PaymentResult> _charges = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _refunds = new(StringComparer.Ordinal);

    public Task<PaymentResult> ChargeAsync(long amountMinor, string currency, string paymentMethodId, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(_charges.GetOrAdd(idempotencyKey, key => paymentMethodId switch
        {
            "pm_card_visa" => Ok(key, "visa", "4242"),
            "pm_card_mastercard" => Ok(key, "mastercard", "4444"),
            "pm_card_amex" => Ok(key, "amex", "8431"),
            "pm_card_chargeDeclined" => Fail("card_declined", "Your card was declined."),
            "pm_card_chargeDeclinedInsufficientFunds" => Fail("insufficient_funds", "Your card has insufficient funds."),
            "pm_card_chargeDeclinedExpiredCard" => Fail("expired_card", "Your card has expired."),
            _ => Fail("invalid_payment_method", "This payment method isn't supported in test mode."),
        }));

    public Task<string> RefundAsync(string intentId, long amountMinor, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(_refunds.GetOrAdd(idempotencyKey, key => $"re_mock_{Hash(key)}"));

    private static PaymentResult Ok(string key, string brand, string last4) => new(true, $"pi_mock_{Hash(key)}", brand, last4, null, null);
    private static PaymentResult Fail(string code, string message) => new(false, null, null, null, code, message);
    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..24].ToLowerInvariant();
}

// ================================================================== Caching

/// <summary>HybridCache: L1 in-process + optional L2 (Redis) with tags and built-in stampede protection.</summary>
internal sealed class HybridCatalogCache(HybridCache cache) : ICatalogCache
{
    public ValueTask<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, string[] tags, TimeSpan ttl, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(key, factory, new HybridCacheEntryOptions { Expiration = ttl, LocalCacheExpiration = ttl < TimeSpan.FromMinutes(2) ? ttl : TimeSpan.FromMinutes(2) }, tags, cancellationToken);

    public ValueTask InvalidateTagAsync(string tag, CancellationToken cancellationToken) => cache.RemoveByTagAsync(tag, cancellationToken);
}

/// <summary>Builds the BM25 index once from the monograph table and shares it (immutable, thread-safe).</summary>
internal sealed class KnowledgeIndexProvider(IServiceScopeFactory scopes) : IKnowledgeIndexProvider, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private KnowledgeIndex? _index;

    public async ValueTask<KnowledgeIndex> GetAsync(CancellationToken cancellationToken)
    {
        if (_index is { } ready) return ready;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_index is not null) return _index;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MedoraDbContext>();
            var sections = await db.MonographSections.AsNoTracking().ToListAsync(cancellationToken);
            _index = new KnowledgeIndex(sections.Select(s => new KnowledgeChunk(s.Id, s.GenericName, s.Title, s.Content, s.Source)));
            return _index;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}

// ================================================================== Background workers

/// <summary>
/// Releases unpaid holds whose 15-minute window elapsed: the order becomes Expired and its reserved
/// stock goes back on the shelf, in one transaction per order. Rowversion makes a race with a
/// last-second payment safe (one of them loses and the other's view wins).
/// </summary>
internal sealed partial class HoldExpiryWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<HoldExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailure(logger, ex); // never let one bad tick kill the worker
            }
        }
    }

    internal async Task<int> SweepAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedoraDbContext>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var ledger = scope.ServiceProvider.GetRequiredService<IStockLedger>();
        var now = clock.GetUtcNow();
        var expired = await db.Orders.Where(o => o.Status == OrderStatus.PendingPayment && o.HoldExpiresAt < now && o.PaymentIntentId == null)
            .OrderBy(o => o.HoldExpiresAt).Take(100).ToListAsync(ct);
        var released = 0;
        foreach (var order in expired)
        {
            try
            {
                await uow.InTransactionAsync(async token =>
                {
                    await ledger.ReleaseAsync(order.BranchId, order.Lines.Select(l => new StockLine(l.ProductId, l.Quantity)).ToList(), token);
                    order.TransitionTo(OrderStatus.Expired, now);
                    await db.SaveChangesAsync(token);
                    return true;
                }, ct);
                released++;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear(); // paid at the last second: the payment wins
            }
        }
        if (released > 0) LogExpired(logger, released);
        return released;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Expired {Count} unpaid orders and released their stock")]
    private static partial void LogExpired(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Hold expiry sweep failed")]
    private static partial void LogFailure(ILogger logger, Exception ex);
}

/// <summary>
/// Transactional-outbox dispatcher. Claims a batch with UPDLOCK + READPAST so several API instances
/// can run it concurrently without double-sending. At-least-once with bounded retries.
/// </summary>
internal sealed partial class OutboxDispatcher(IServiceScopeFactory scopes, TimeProvider clock, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private const int MaxAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailure(logger, ex);
            }
        }
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedoraDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async token =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(token);
            var batch = await db.OutboxMessages
                .FromSqlInterpolated($"SELECT TOP ({BatchSize}) * FROM [OutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK) WHERE [ProcessedAt] IS NULL AND [Attempts] < {MaxAttempts} ORDER BY [OccurredAt]")
                .ToListAsync(token);
            if (batch.Count == 0) return;
            foreach (var message in batch)
            {
                try
                {
                    if (message.Type != "email") throw new InvalidOperationException($"Unknown outbox message type {message.Type}");
                    var payload = JsonSerializer.Deserialize<EmailPayload>(message.Payload)!;
                    await email.SendAsync(payload.To, payload.Subject, payload.Body, token);
                    message.MarkProcessed(clock.GetUtcNow());
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    message.MarkFailed(ex.Message);
                }
            }
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
        }, ct);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox dispatch failed")]
    private static partial void LogFailure(ILogger logger, Exception ex);
}

/// <summary>Development email sender: writes the message to the log. Swap for SMTP / Azure Communication Services.</summary>
internal sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        LogEmail(logger, to, subject, body);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[email] To: {To} | {Subject}\n{Body}")]
    private static partial void LogEmail(ILogger logger, string to, string subject, string body);
}
