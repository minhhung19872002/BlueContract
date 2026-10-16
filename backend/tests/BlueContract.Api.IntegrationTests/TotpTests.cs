using System.Text;
using BlueContract.Infrastructure.Identity;
using Shouldly;

namespace BlueContract.Api.IntegrationTests;

public class TotpTests
{
    // RFC 6238 Appendix B, SHA-1 seed "12345678901234567890" (6-digit truncation of the published 8-digit values).
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Matches_rfc6238_vectors(long unixTime, string expected)
    {
        var key = Encoding.ASCII.GetBytes("12345678901234567890");

        TotpService.Compute(key, unixTime / 30).ShouldBe(expected);
    }

    [Fact]
    public void Base32_roundtrip()
    {
        var bytes = Encoding.ASCII.GetBytes("12345678901234567890");

        TotpService.ToBase32(bytes).ShouldBe("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");
        TotpService.FromBase32("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ").ShouldBe(bytes);
    }

    [Fact]
    public void Verify_accepts_one_step_drift_only()
    {
        var service = new TotpService();
        var secret = service.GenerateSecret();
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var key = TotpService.FromBase32(secret);

        service.Verify(secret, TotpService.Compute(key, now.ToUnixTimeSeconds() / 30), now).ShouldBeTrue();
        service.Verify(secret, TotpService.Compute(key, (now.ToUnixTimeSeconds() / 30) - 1), now).ShouldBeTrue();
        service.Verify(secret, TotpService.Compute(key, (now.ToUnixTimeSeconds() / 30) - 3), now).ShouldBeFalse();
        service.Verify(secret, "abc123", now).ShouldBeFalse();
    }
}
