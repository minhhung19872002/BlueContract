using BlueContract.Api.Startup;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Guards;

/// <summary>AUDIT-1 §13.6: Production refuses the development switches and default secrets of docker-compose.yml.</summary>
public class ProductionGuardTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    /// <summary>The values docker-compose.yml uses when .env sets nothing.</summary>
    private static Dictionary<string, string?> DevDefaults() => new()
    {
        ["Seed:Demo"] = "true",
        ["Seed:AllowReset"] = "true",
        ["Signing:EnableMockProviders"] = "true",
        ["HisInbound:DevEndpoints"] = "true",
        ["Hangfire:DashboardEnabled"] = "true",
        ["Auth:SigningKey"] = "dev-only-signing-key-change-me-0123456789abcdef",
        ["OnlyOffice:JwtSecret"] = "onlyoffice_dev_secret_change_me_32chars",
        ["Storage:SecretKey"] = "blueprocure_s3_dev",
        ["HisInbound:Clients:0:Name"] = "HIS",
        ["HisInbound:Clients:0:ApiKey"] = "his-dev-key",
        ["HisInbound:Clients:0:Secret"] = "his-dev-secret-change-me-0123456789",
        ["ConnectionStrings:Default"] = "Host=postgres;Database=blueprocure;Username=blueprocure;Password=blueprocure_dev",
        ["App:PublicBaseUrl"] = "http://localhost:47080",
    };

    [Fact]
    public void Every_development_default_is_reported()
    {
        var problems = ProductionGuard.Problems(Config(DevDefaults()));
        problems.Count.ShouldBe(12);
        problems.ShouldContain(p => p.Contains("SEED_DEMO"));
        problems.ShouldContain(p => p.Contains("AUTH_SIGNING_KEY"));
        problems.ShouldContain(p => p.Contains("POSTGRES_PASSWORD"));
        problems.ShouldContain(p => p.Contains("PUBLIC_BASE_URL"));
    }

    [Fact]
    public void A_hardened_configuration_starts()
    {
        var c = DevDefaults();
        foreach (var flag in new[] { "Seed:Demo", "Seed:AllowReset", "Signing:EnableMockProviders", "HisInbound:DevEndpoints", "Hangfire:DashboardEnabled" })
        {
            c[flag] = "false";
        }

        c["Auth:SigningKey"] = "Q3x9Lw2pZr7Tn4Vb8Kc1Hd6Fs0Ym5Ga2Je";
        c["OnlyOffice:JwtSecret"] = "Pk4Wq8Rz1Lm7Xn3Bv6Cy0Ht5Jg2Df9Sa";
        c["Storage:SecretKey"] = "s3-Xq7Lp2Wz9Rk4Tm";
        c["HisInbound:Clients:0:ApiKey"] = "his-key-7Qp2Lx9Wr4";
        c["HisInbound:Clients:0:Secret"] = "Hz8Kq3Lp6Wx1Rm9Tn4Vb7Cy2Jd5Gf0Sa";
        c["ConnectionStrings:Default"] = "Host=postgres;Database=blueprocure;Username=blueprocure;Password=Pg-7Xq2Lw9Rz4Tk";
        c["App:PublicBaseUrl"] = "https://muasam.benhvien.vn";
        ProductionGuard.Problems(Config(c)).ShouldBeEmpty();
    }
}
