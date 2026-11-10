using Mawrid.Domain.Identity;

namespace Mawrid.Domain.Tests.Identity;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    [Fact]
    public void A_new_token_is_valid_until_it_expires()
    {
        var token = RefreshToken.StartFamily(Guid.NewGuid(), "hash-1", Now, Lifetime);

        token.Check(Now).Should().Be(RefreshTokenCheck.Valid);
        token.Check(Now + Lifetime - TimeSpan.FromSeconds(1)).Should().Be(RefreshTokenCheck.Valid);
        token.Check(Now + Lifetime).Should().Be(RefreshTokenCheck.Expired);
    }

    [Fact]
    public void Rotate_marks_the_old_token_used_and_keeps_the_family()
    {
        var token = RefreshToken.StartFamily(Guid.NewGuid(), "hash-1", Now, Lifetime);

        var next = token.Rotate("hash-2", Now.AddMinutes(5), Lifetime);

        next.FamilyId.Should().Be(token.FamilyId);
        next.UserId.Should().Be(token.UserId);
        token.ReplacedByTokenId.Should().Be(next.Id);
        token.Check(Now.AddMinutes(6)).Should().Be(RefreshTokenCheck.Reused);
        next.Check(Now.AddMinutes(6)).Should().Be(RefreshTokenCheck.Valid);
    }

    [Fact]
    public void A_revoked_token_reports_revoked_even_if_it_was_used()
    {
        var token = RefreshToken.StartFamily(Guid.NewGuid(), "hash-1", Now, Lifetime);
        token.Rotate("hash-2", Now, Lifetime);

        token.Revoke(Now.AddMinutes(1));

        token.Check(Now.AddMinutes(2)).Should().Be(RefreshTokenCheck.Revoked);
    }

    [Fact]
    public void Only_a_valid_token_can_be_rotated()
    {
        var token = RefreshToken.StartFamily(Guid.NewGuid(), "hash-1", Now, Lifetime);

        var rotateExpired = () => token.Rotate("hash-2", Now + Lifetime, Lifetime);

        rotateExpired.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Revoke_keeps_the_first_revocation_time()
    {
        var token = RefreshToken.StartFamily(Guid.NewGuid(), "hash-1", Now, Lifetime);

        token.Revoke(Now.AddMinutes(1));
        token.Revoke(Now.AddMinutes(9));

        token.RevokedAtUtc.Should().Be(Now.AddMinutes(1));
    }
}
