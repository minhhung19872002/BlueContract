using System.Linq.Expressions;
using System.Net;
using BlueContract.Application.Auth;
using BlueContract.Application.Common;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Organizations;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Admin;

public sealed record SupplierDto(
    Guid Id,
    string TaxCode,
    string Name,
    string? ShortName,
    string? Address,
    string? RepresentativeName,
    string? RepresentativeTitle,
    string? AuthorizationDocument,
    string? BankAccountNumber,
    string? BankName,
    string? NotificationEmail,
    string? Phone,
    bool IsActive,
    int AccountCount);

public sealed record SupplierAccountRequest(string UserName, string FullName, string Email);

public sealed record SaveSupplierRequest(
    string TaxCode,
    string Name,
    string? ShortName,
    string? Address,
    string? RepresentativeName,
    string? RepresentativeTitle,
    string? AuthorizationDocument,
    string? BankAccountNumber,
    string? BankName,
    string? NotificationEmail,
    string? Phone,
    bool IsActive = true,
    SupplierAccountRequest? AdminAccount = null);

internal sealed class SaveSupplierRequestValidator : AbstractValidator<SaveSupplierRequest>
{
    public SaveSupplierRequestValidator()
    {
        RuleFor(x => x.TaxCode).NotEmpty().WithName("Mã số thuế")
            .Matches(@"^\s*\d{10}(-\d{3})?\s*$").WithMessage("Mã số thuế gồm 10 chữ số hoặc 13 ký tự dạng 0123456789-001.");
        RuleFor(x => x.Name).NotEmpty().WithName("Tên nhà cung cấp").MaximumLength(300);
        RuleFor(x => x.NotificationEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.NotificationEmail)).WithName("Email nhận thông báo");
        When(x => x.AdminAccount is not null, () =>
        {
            RuleFor(x => x.AdminAccount!.UserName).NotEmpty().WithName("Tên đăng nhập")
                .Matches("^[A-Za-z0-9._-]{3,64}$").WithMessage("Tên đăng nhập 3–64 ký tự, chỉ gồm chữ, số, dấu chấm, gạch dưới, gạch ngang.");
            RuleFor(x => x.AdminAccount!.FullName).NotEmpty().WithName("Họ tên người quản trị");
            RuleFor(x => x.AdminAccount!.Email).NotEmpty().EmailAddress().WithName("Email người quản trị");
        });
    }
}

public sealed class SupplierService(
    IAppDbContext db,
    IPasswordService passwords,
    IEmailSender email,
    IAppLinkBuilder links)
{
    private static readonly Dictionary<string, Expression<Func<Supplier, object?>>> Sorts = new()
    {
        ["taxCode"] = x => x.TaxCode,
        ["name"] = x => x.Name,
    };

    public Task<PagedResult<SupplierDto>> ListAsync(PageQuery q, bool? isActive, CancellationToken ct)
    {
        var query = db.Suppliers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(s => EF.Functions.ILike(s.TaxCode, p) || EF.Functions.ILike(s.Name, p) || EF.Functions.ILike(s.ShortName!, p));
        }

        if (isActive is not null)
        {
            query = query.Where(s => s.IsActive == isActive);
        }

        return Project(query.ApplySort(q, Sorts, "name")).ToPagedAsync(q, ct);
    }

    public async Task<Result<SupplierDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await Project(db.Suppliers.AsNoTracking().Where(s => s.Id == id)).FirstOrDefaultAsync(ct);
        return dto is null ? Error.NotFound() : dto;
    }

    /// <summary>Creates a supplier; optionally provisions its SUPPLIER_ADMIN account and emails the credentials (spec §6.6).</summary>
    public async Task<Result<SupplierDto>> CreateAsync(SaveSupplierRequest r, CancellationToken ct)
    {
        var taxCode = Supplier.NormalizeTaxCode(r.TaxCode);
        if (await db.Suppliers.IgnoreQueryFilters().AnyAsync(s => s.TaxCode == taxCode, ct))
        {
            return Error.Conflict("SUPPLIER_TAX_CODE_EXISTS");
        }

        var supplier = new Supplier(taxCode, r.Name);
        Apply(supplier, r);
        db.Suppliers.Add(supplier);

        (User User, string Password)? account = null;
        if (r.AdminAccount is not null)
        {
            var created = await CreateAccountAsync(supplier, r.AdminAccount, RoleCatalog.SupplierAdmin, ct);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            account = created.Value;
        }

        await db.SaveChangesAsync(ct);

        if (account is { } a)
        {
            await SendCredentialsAsync(supplier, a.User, a.Password, ct);
        }

        return await GetAsync(supplier.Id, ct);
    }

    public async Task<Result<SupplierDto>> UpdateAsync(Guid id, SaveSupplierRequest r, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null)
        {
            return Error.NotFound();
        }

        Apply(supplier, r);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Provisions an additional account for an existing supplier ("cấp tài khoản").</summary>
    public async Task<Result<Guid>> CreateSupplierAccountAsync(Guid supplierId, SupplierAccountRequest r, bool asAdmin, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId, ct);
        if (supplier is null)
        {
            return Error.NotFound();
        }

        var created = await CreateAccountAsync(supplier, r, asAdmin ? RoleCatalog.SupplierAdmin : RoleCatalog.SupplierUser, ct);
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        await db.SaveChangesAsync(ct);
        await SendCredentialsAsync(supplier, created.Value.User, created.Value.Password, ct);
        return created.Value.User.Id;
    }

    internal async Task<Result<(User User, string Password)>> CreateAccountAsync(Supplier supplier, SupplierAccountRequest r, string roleCode, CancellationToken ct)
    {
        var userName = User.NormalizeUserName(r.UserName);
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.UserName == userName, ct))
        {
            return Error.Conflict("USER_NAME_EXISTS");
        }

        var roleId = await db.Roles.Where(x => x.Code == roleCode).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (roleId == Guid.Empty)
        {
            return Error.Validation("ROLE_NOT_FOUND");
        }

        var user = new User(userName, r.FullName, r.Email, UserType.Supplier, supplier.Id);
        var password = PasswordPolicy.GenerateTemporary();
        user.SetPasswordHash(passwords.Hash(user, password), mustChange: true);
        user.SetRoles([roleId]);
        db.Users.Add(user);
        return (user, password);
    }

    internal async Task SendCredentialsAsync(Supplier supplier, User user, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        var org = await db.Organizations.AsNoTracking().Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "Bệnh viện";
        var loginUrl = links.Build("/dang-nhap");
        var html = $"""
            <p>Kính gửi {WebUtility.HtmlEncode(user.FullName)} ({WebUtility.HtmlEncode(supplier.Name)}),</p>
            <p>{WebUtility.HtmlEncode(org)} đã cấp tài khoản truy cập <b>Cổng Quản lý &amp; Thanh toán Mua sắm Hàng hóa Trực tuyến</b> cho Quý công ty.</p>
            <ul>
              <li>Tên đăng nhập: <b>{WebUtility.HtmlEncode(user.UserName)}</b></li>
              <li>Mật khẩu tạm thời: <b>{WebUtility.HtmlEncode(password)}</b></li>
            </ul>
            <p>Vui lòng đăng nhập tại <a href="{loginUrl}">{loginUrl}</a> và đổi mật khẩu ngay trong lần đăng nhập đầu tiên.</p>
            <p>Trân trọng.</p>
            """;
        await email.SendAsync(new EmailMessage([user.Email], "Thông tin tài khoản Cổng mua sắm trực tuyến", html), ct);
    }

    private static void Apply(Supplier s, SaveSupplierRequest r) =>
        s.Update(r.Name, r.ShortName, r.Address, r.RepresentativeName, r.RepresentativeTitle, r.AuthorizationDocument,
            r.BankAccountNumber, r.BankName, r.NotificationEmail, r.Phone, r.IsActive);

    private IQueryable<SupplierDto> Project(IQueryable<Supplier> query) =>
        query.Select(s => new SupplierDto(
            s.Id, s.TaxCode, s.Name, s.ShortName, s.Address, s.RepresentativeName, s.RepresentativeTitle, s.AuthorizationDocument,
            s.BankAccountNumber, s.BankName, s.NotificationEmail, s.Phone, s.IsActive,
            db.Users.Count(u => u.SupplierId == s.Id)));
}
