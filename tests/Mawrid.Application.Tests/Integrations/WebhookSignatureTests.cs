using Mawrid.Application.Integrations;

namespace Mawrid.Application.Tests.Integrations;

public class WebhookSignatureTests
{
    // Checked with: python3 -c "import hmac,hashlib; print(hmac.new(b'whsec_test', b'1700000000.{\"id\":1}', hashlib.sha256).hexdigest())"
    private const string Expected = "sha256=2f441ba4b3b2d50d28a9ab9d9fd8880376ecd1eb5d0435401553f5d8d0a5dcf8";

    [Fact]
    public void The_signature_is_hmac_sha256_of_timestamp_dot_body()
    {
        WebhookSignature.Compute("whsec_test", 1_700_000_000, "{\"id\":1}").Should().Be(Expected);
    }

    [Fact]
    public void Verify_refuses_a_changed_body_timestamp_or_secret()
    {
        WebhookSignature.Verify("whsec_test", 1_700_000_000, "{\"id\":1}", Expected).Should().BeTrue();
        WebhookSignature.Verify("whsec_test", 1_700_000_000, "{\"id\":2}", Expected).Should().BeFalse();
        WebhookSignature.Verify("whsec_test", 1_700_000_001, "{\"id\":1}", Expected).Should().BeFalse();
        WebhookSignature.Verify("other", 1_700_000_000, "{\"id\":1}", Expected).Should().BeFalse();
    }
}
