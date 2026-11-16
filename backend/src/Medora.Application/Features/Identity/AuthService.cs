using System.Security.Cryptography;
using System.Text;
using Medora.Application.Abstractions;
using Medora.Application.Common;
using Medora.Application.Contracts;
using Medora.Domain.Common;
using Medora.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Medora.Application.Features.Identity;

public sealed class IdentitySettings
{
    public const string Section = "Identity";
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);
    /// <summary>Parallel requests may present the same refresh token; within this window it's not treated as theft.</summary>
    public TimeSpan RefreshReuseGrace { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Account lifecycle: lookup, login (lockout), register, rotating refresh with reuse detection, logout, profile.</summary>
public sealed partial class AuthService(
    IAppDbContext db,
    IPasswordHasher hasher,
    IAccessTokenIssuer tokens,
    ICurrentUser currentUser,
    IOptions<IdentitySettings> options,
    TimeProvider clock,
    ILogger<AuthService> logger)
{
    private readonly IdentitySettings _settings = options.Value;

    public async Task<LookupResponse> LookupAsync(LookupRequest request, CancellationToken ct)
    {
        if (!Guard.IsEmail(request.Email)) return new LookupResponse(false);
        var normalized = User.Normalize(request.Email);
        return new LookupResponse(await db.Users.AsNoTracking().AnyAsync(u => u.NormalizedEmail == normalized, ct));
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        if (!Guard.IsEmail(request.Email) || string.IsNullOrEmpty(request.Password) || request.Password.Length > PasswordPolicy.MaxLength) return Errors.InvalidCredentials;

        var now = clock.GetUtcNow();
        var normalized = User.Normalize(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is null)
        {
            hasher.VerifyDummy(request.Password); // same cost as a real check: no user enumeration by timing
            return Errors.InvalidCredentials;
        }
        if (user.IsLockedOut(now))
        {
            hasher.VerifyDummy(request.Password);
            return Errors.LockedOut;
        }
        if (!hasher.Verify(user.PasswordHash, request.Password, out var needsRehash))
        {
            user.RegisterFailedLogin(now);
            await db.SaveChangesAsync(ct);
            if (user.IsLockedOut(now)) LogLockout(logger, user.Id);
            return Errors.InvalidCredentials;
        }

        user.RegisterSuccessfulLogin();
        if (needsRehash) user.SetPasswordHash(hasher.Hash(request.Password));
        return await IssueAsync(user, Guid.CreateVersion7(), now + _settings.RefreshTokenLifetime, ct);
    }

    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        if (!Guard.IsName(request.FirstName) || !Guard.IsName(request.LastName)) return Errors.Validation("Enter your first and last name.");
        if (!Guard.IsEmail(request.Email)) return Errors.Validation("Enter a valid email.");
        var now = clock.GetUtcNow();
        if (request.BirthDate.AddYears(16) > DateOnly.FromDateTime(now.UtcDateTime)) return Errors.Validation("You need to be at least 16 to create an account.");
        if (!PasswordPolicy.IsAcceptable(request.Password, request.Email, request.FirstName)) return Errors.WeakPassword;

        var normalized = User.Normalize(request.Email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct)) return Errors.EmailTaken;

        var user = User.Register(request.FirstName.Trim(), request.LastName.Trim(), request.Email.Trim(), request.BirthDate, now);
        user.SetPasswordHash(hasher.Hash(request.Password));
        db.Users.Add(user);
        try
        {
            return await IssueAsync(user, Guid.CreateVersion7(), now + _settings.RefreshTokenLifetime, ct);
        }
        catch (DbUpdateException)
        {
            return Errors.EmailTaken; // lost a race against the unique index
        }
    }

    public async Task<Result<AuthResponse>> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.RefreshToken) || request.RefreshToken.Length > 200) return Errors.InvalidRefreshToken;
        var now = clock.GetUtcNow();
        var hash = Sha256(request.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.ExpiresAt <= now || token.RevokedAt is not null) return Errors.InvalidRefreshToken;

        if (token.UsedAt is { } usedAt)
        {
            if (now - usedAt > _settings.RefreshReuseGrace)
            {
                // REUSE DETECTED: revoke the whole family (set-based, one statement).
                await db.RefreshTokens.Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
                LogReuse(logger, token.UserId);
                return Errors.InvalidRefreshToken;
            }
        }
        else
        {
            token.MarkUsed(now);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (user is null) return Errors.InvalidRefreshToken;
        // Rotation keeps the family's absolute expiry: refreshing can't extend a session forever.
        return await IssueAsync(user, token.FamilyId, token.ExpiresAt, ct);
    }

    public async Task LogoutAsync(RefreshRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.RefreshToken)) return;
        var hash = Sha256(request.RefreshToken);
        var family = await db.RefreshTokens.AsNoTracking().Where(t => t.TokenHash == hash).Select(t => (Guid?)t.FamilyId).FirstOrDefaultAsync(ct);
        if (family is null) return;
        var now = clock.GetUtcNow();
        await db.RefreshTokens.Where(t => t.FamilyId == family && t.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    public async Task<Result<UserDto>> MeAsync(CancellationToken ct)
    {
        var id = currentUser.RequireUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? Errors.NotFound("User") : user.ToDto();
    }

    public async Task<Result<UserDto>> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct)
    {
        if (!Guard.IsName(request.FirstName) || !Guard.IsName(request.LastName)) return Errors.Validation("Enter your first and last name.");
        if (request.Allergies is { Length: > 500 }) return Errors.Validation("Keep allergies under 500 characters.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequireUserId(), ct);
        if (user is null) return Errors.NotFound("User");
        user.Rename(request.FirstName.Trim(), request.LastName.Trim());
        user.SetAllergies(request.Allergies);
        await db.SaveChangesAsync(ct);
        return user.ToDto();
    }

    private async Task<Result<AuthResponse>> IssueAsync(User user, Guid familyId, DateTimeOffset refreshExpiresAt, CancellationToken ct)
    {
        var refresh = NewToken();
        db.RefreshTokens.Add(new RefreshToken(Sha256(refresh), user.Id, familyId, refreshExpiresAt));
        await db.SaveChangesAsync(ct);
        var access = tokens.Issue(user);
        return new AuthResponse(user.ToDto(), access.Token, access.ExpiresAt, refresh, refreshExpiresAt);
    }

    /// <summary>256 bits from the OS CSPRNG, URL-safe.</summary>
    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Tokens are stored hashed: a leaked table can't be replayed.</summary>
    internal static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account {UserId} locked out after repeated failed logins")]
    private static partial void LogLockout(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token reuse detected for {UserId}; token family revoked")]
    private static partial void LogReuse(ILogger logger, string userId);
}
