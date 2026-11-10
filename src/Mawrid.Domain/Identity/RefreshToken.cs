using Mawrid.Domain.Common;

namespace Mawrid.Domain.Identity;

/// <summary>
/// A long-lived token used to get a new access token. Only a SHA-256 hash is stored.
/// Each refresh replaces the token with a new one in the same <see cref="FamilyId"/>.
/// If a used token comes back, someone copied it: the whole family is revoked.
/// </summary>
public sealed class RefreshToken : Entity<Guid>
{
    private RefreshToken(Guid id, Guid userId, string tokenHash, Guid familyId, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    private RefreshToken()
    {
        TokenHash = null!;
    }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; }

    /// <summary>All tokens created from one login share a family.</summary>
    public Guid FamilyId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? UsedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    /// <summary>Creates the first token of a new family (at login).</summary>
    public static RefreshToken StartFamily(Guid userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime) =>
        new(Guid.CreateVersion7(), userId, tokenHash, Guid.CreateVersion7(), now, now + lifetime);

    /// <summary>Decides what to do when this token is presented.</summary>
    public RefreshTokenCheck Check(DateTimeOffset now)
    {
        if (RevokedAtUtc is not null)
        {
            return RefreshTokenCheck.Revoked;
        }

        if (UsedAtUtc is not null)
        {
            return RefreshTokenCheck.Reused;
        }

        return now >= ExpiresAtUtc ? RefreshTokenCheck.Expired : RefreshTokenCheck.Valid;
    }

    /// <summary>Uses this token and returns its replacement in the same family.</summary>
    public RefreshToken Rotate(string newTokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        if (Check(now) != RefreshTokenCheck.Valid)
        {
            throw new InvalidOperationException("Only a valid refresh token can be rotated.");
        }

        var next = new RefreshToken(Guid.CreateVersion7(), UserId, newTokenHash, FamilyId, now, now + lifetime);
        UsedAtUtc = now;
        ReplacedByTokenId = next.Id;
        return next;
    }

    public void Revoke(DateTimeOffset now) => RevokedAtUtc ??= now;
}

public enum RefreshTokenCheck
{
    Valid,
    Expired,
    Revoked,

    /// <summary>The token was already used once. This means it was stolen or copied.</summary>
    Reused,
}
