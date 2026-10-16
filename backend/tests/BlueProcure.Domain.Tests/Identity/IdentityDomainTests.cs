using BlueProcure.Domain.Common;
using BlueProcure.Domain.Identity;
using BlueProcure.Domain.Organizations;
using Shouldly;

namespace BlueProcure.Domain.Tests.Identity;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Locks_after_max_failed_attempts_and_unlocks_after_duration()
    {
        var user = new User("ketoan", "Kế toán", null, UserType.Internal, null);

        for (var i = 0; i < 4; i++)
        {
            user.RegisterFailedLogin(Now, 5, TimeSpan.FromMinutes(15)).ShouldBeFalse();
        }

        user.RegisterFailedLogin(Now, 5, TimeSpan.FromMinutes(15)).ShouldBeTrue();
        user.IsLockedOut(Now.AddMinutes(14)).ShouldBeTrue();
        user.IsLockedOut(Now.AddMinutes(15)).ShouldBeFalse();
    }

    [Fact]
    public void Successful_login_resets_failure_counter()
    {
        var user = new User("ketoan", "Kế toán", null, UserType.Internal, null);
        user.RegisterFailedLogin(Now, 5, TimeSpan.FromMinutes(15));

        user.RegisterSuccessfulLogin(Now);

        user.FailedLoginCount.ShouldBe(0);
        user.LastLoginAt.ShouldBe(Now);
    }

    [Fact]
    public void Password_change_rotates_security_stamp()
    {
        var user = new User("ketoan", "Kế toán", null, UserType.Internal, null);
        var before = user.SecurityStamp;

        user.SetPasswordHash("hash", mustChange: false);

        user.SecurityStamp.ShouldNotBe(before);
    }

    [Fact]
    public void Supplier_user_requires_supplier_and_internal_user_drops_it()
    {
        Should.Throw<DomainException>(() => new User("ncc", "NCC", null, UserType.Supplier, null)).Code.ShouldBe("USER_SUPPLIER_REQUIRED");
        new User("noi", "Nội bộ", null, UserType.Internal, Guid.NewGuid()).SupplierId.ShouldBeNull();
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("có dấu")]
    [InlineData("a b c")]
    public void Rejects_invalid_user_names(string userName) =>
        Should.Throw<DomainException>(() => User.NormalizeUserName(userName)).Code.ShouldBe("USER_NAME_INVALID");

    [Fact]
    public void User_name_is_normalized_to_lower_case() => User.NormalizeUserName(" NCC1.User ").ShouldBe("ncc1.user");

    [Fact]
    public void SetRoles_adds_and_removes_without_duplicates()
    {
        var user = new User("user1", "U", null, UserType.Internal, null);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        user.SetRoles([a, b, a]);
        user.Roles.Count.ShouldBe(2);

        user.SetRoles([b]);
        user.Roles.Single().RoleId.ShouldBe(b);
    }

    [Fact]
    public void Two_factor_cannot_be_enabled_without_secret()
    {
        var user = new User("user1", "U", null, UserType.Internal, null);

        Should.Throw<DomainException>(user.EnableTwoFactor).Code.ShouldBe("TWO_FACTOR_NOT_SETUP");

        user.SetPendingTwoFactorSecret("SECRET");
        user.EnableTwoFactor();
        user.TwoFactorEnabled.ShouldBeTrue();
    }
}

public class RoleTests
{
    [Fact]
    public void Unknown_permission_is_rejected()
    {
        var role = new Role("TEST", "Test", null, false);

        Should.Throw<DomainException>(() => role.SetPermissions(["contract.fly"])).Code.ShouldBe("PERMISSION_UNKNOWN");
    }

    [Fact]
    public void Catalog_roles_only_reference_known_permissions()
    {
        foreach (var definition in RoleCatalog.All)
        {
            definition.Permissions.ShouldAllBe(p => PermissionCatalog.Exists(p), definition.Code);
        }
    }

    [Fact]
    public void Supplier_roles_never_get_internal_admin_permissions()
    {
        var supplierRoles = RoleCatalog.All.Where(r => r.Code.StartsWith("SUPPLIER_", StringComparison.Ordinal));

        foreach (var role in supplierRoles)
        {
            role.Permissions.ShouldNotContain(p => p.StartsWith("admin.", StringComparison.Ordinal) || p == Permissions.AuditView);
        }
    }

    [Fact]
    public void Only_clerk_can_seal_by_default()
    {
        RoleCatalog.All.Where(r => r.Permissions.Contains(Permissions.ContractSeal)).Select(r => r.Code).ShouldBe([RoleCatalog.Clerk]);
    }
}

public class SigningDelegationTests
{
    private static readonly DateTimeOffset From = new(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public void Effective_only_inside_period_and_until_revoked()
    {
        var d = new SigningDelegation(Guid.NewGuid(), Guid.NewGuid(), "Giám đốc", "Phó Giám đốc", From, From.AddDays(7), null);

        d.IsEffectiveAt(From.AddSeconds(-1)).ShouldBeFalse();
        d.IsEffectiveAt(From).ShouldBeTrue();
        d.IsEffectiveAt(From.AddDays(7)).ShouldBeFalse();

        d.Revoke(From.AddDays(1));
        d.IsEffectiveAt(From.AddDays(2)).ShouldBeFalse();
    }

    [Fact]
    public void Caption_reads_kt_giam_doc()
    {
        var d = new SigningDelegation(Guid.NewGuid(), Guid.NewGuid(), "Giám đốc", "Phó Giám đốc", From, From.AddDays(7), null);

        d.SignatureCaption().ShouldBe(("KT. GIÁM ĐỐC", "PHÓ GIÁM ĐỐC"));
    }

    [Fact]
    public void Rejects_self_delegation_and_bad_period()
    {
        var id = Guid.NewGuid();
        Should.Throw<DomainException>(() => new SigningDelegation(id, id, "A", "B", From, From.AddDays(1), null)).Code.ShouldBe("DELEGATION_SAME_USER");
        Should.Throw<DomainException>(() => new SigningDelegation(id, Guid.NewGuid(), "A", "B", From, From, null)).Code.ShouldBe("DELEGATION_INVALID_PERIOD");
    }
}

public class SupplierTests
{
    [Theory]
    [InlineData("0101234567", "0101234567")]
    [InlineData(" 0101234567-001 ", "0101234567-001")]
    public void Accepts_valid_tax_codes(string input, string expected) => Supplier.NormalizeTaxCode(input).ShouldBe(expected);

    [Theory]
    [InlineData("123")]
    [InlineData("01012345678")]
    [InlineData("0101234567-1")]
    public void Rejects_invalid_tax_codes(string input) =>
        Should.Throw<DomainException>(() => Supplier.NormalizeTaxCode(input)).Code.ShouldBe("SUPPLIER_TAX_CODE_INVALID");
}
