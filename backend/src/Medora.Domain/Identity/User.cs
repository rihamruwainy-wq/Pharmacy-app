namespace Medora.Domain.Identity;

/// <summary>Role names carried in the JWT <c>role</c> claim.</summary>
public static class Roles
{
    public const string Customer = "customer";
    public const string Pharmacist = "pharmacist";
    public const string Admin = "admin";
}

public sealed class User
{
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private User() { }

    public static User Register(string firstName, string lastName, string email, DateOnly birthDate, DateTimeOffset now) => new()
    {
        Id = Common.Ids.New("usr"),
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        NormalizedEmail = Normalize(email),
        BirthDate = birthDate,
        Roles = [Identity.Roles.Customer],
        CreatedAt = now,
    };

    /// <summary>Seed/demo accounts with a fixed id and role (pharmacists are tied to a branch).</summary>
    public static User Seeded(string id, string firstName, string lastName, string email, string role, string? branchId, DateTimeOffset createdAt)
    {
        var user = Register(firstName, lastName, email, new DateOnly(1990, 1, 1), createdAt);
        user.Id = id;
        user.Roles = [role];
        user.BranchId = branchId;
        return user;
    }

    public string Id { get; private set; } = default!;
    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    /// <summary>Upper-invariant, unique index: case-insensitive lookups with a plain index seek.</summary>
    public string NormalizedEmail { get; private set; } = default!;
    public string PasswordHash { get; private set; } = string.Empty;
    public List<string> Roles { get; private set; } = [];
    /// <summary>Staff only: the branch a pharmacist works at (scopes their queue).</summary>
    public string? BranchId { get; private set; }
    public DateOnly BirthDate { get; private set; }
    /// <summary>Free-text allergies the customer declared (shown to the pharmacist at verification).</summary>
    public string? Allergies { get; private set; }
    public int FailedLogins { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static string Normalize(string email) => email.Trim().ToUpperInvariant();

    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is { } until && until > now;
    public bool IsStaff => Roles.Contains(Identity.Roles.Pharmacist) || Roles.Contains(Identity.Roles.Admin);

    public void SetPasswordHash(string hash) => PasswordHash = hash;
    public void SetAllergies(string? allergies) => Allergies = string.IsNullOrWhiteSpace(allergies) ? null : allergies.Trim();

    public void RegisterFailedLogin(DateTimeOffset now)
    {
        FailedLogins++;
        if (FailedLogins >= MaxFailedLogins)
        {
            LockedUntil = now + LockoutDuration;
            FailedLogins = 0;
        }
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLogins = 0;
        LockedUntil = null;
    }

    public void Rename(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }
}

/// <summary>
/// Rotating refresh token, stored HASHED. Each login starts a family; each refresh uses the token
/// once and issues a successor. Replaying a used token outside the grace window revokes the family.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken() { }

    public RefreshToken(string tokenHash, string userId, Guid familyId, DateTimeOffset expiresAt)
    {
        TokenHash = tokenHash; UserId = userId; FamilyId = familyId; ExpiresAt = expiresAt;
    }

    public string TokenHash { get; private set; } = default!;
    public string UserId { get; private set; } = default!;
    public Guid FamilyId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public void MarkUsed(DateTimeOffset now) => UsedAt ??= now;
}

/// <summary>NIST 800-63B style: length over complexity, no personal info.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static bool IsAcceptable(string? password, string? email = null, string? firstName = null)
    {
        if (password is null || password.Length is < MinLength or > MaxLength) return false;
        if (!password.Any(c => !char.IsAsciiLetter(c))) return false;
        var lower = password.ToLowerInvariant();
        string?[] personal = [email?.Split('@')[0], firstName];
        return !personal.Any(t => t is { Length: >= 3 } && lower.Contains(t.Trim().ToLowerInvariant(), StringComparison.Ordinal));
    }
}

public sealed class Favorite
{
    private Favorite() { }

    public Favorite(string userId, string productId, DateTimeOffset createdAt)
    {
        UserId = userId; ProductId = productId; CreatedAt = createdAt;
    }

    public string UserId { get; private set; } = default!;
    public string ProductId { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>Transactional outbox: side effects (emails, push) saved in the SAME transaction as the state change.</summary>
public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public OutboxMessage(string type, string payload, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(); Type = type; Payload = payload; OccurredAt = now;
    }

    public Guid Id { get; private set; }
    public string Type { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    public void MarkProcessed(DateTimeOffset now) => ProcessedAt = now;

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }
}

/// <summary>
/// Append-only audit trail for regulated actions (prescription verification, dispensing, refunds,
/// AI use by staff). Pharmacies must be able to show who did what, when.
/// </summary>
public sealed class AuditEntry
{
    private AuditEntry() { }

    public AuditEntry(string actorId, string action, string subjectId, string? detail, DateTimeOffset at)
    {
        Id = Guid.CreateVersion7(); ActorId = actorId; Action = action; SubjectId = subjectId;
        Detail = detail is { Length: > 2000 } ? detail[..2000] : detail;
        At = at;
    }

    public Guid Id { get; private set; }
    public string ActorId { get; private set; } = default!;
    public string Action { get; private set; } = default!;
    public string SubjectId { get; private set; } = default!;
    public string? Detail { get; private set; }
    public DateTimeOffset At { get; private set; }
}
