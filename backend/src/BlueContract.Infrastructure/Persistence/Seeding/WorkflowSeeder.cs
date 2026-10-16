using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using A = BlueContract.Domain.Workflows.WorkflowActionType;
using T = BlueContract.Domain.Workflows.AssigneeType;

namespace BlueContract.Infrastructure.Persistence.Seeding;

/// <summary>Default signing workflows (spec §6). Admins adjust them in "Cấu hình luồng ký"; existing ones are never overwritten.</summary>
internal sealed class WorkflowSeeder(AppDbContext db, ILogger<WorkflowSeeder> logger)
{
    private static WorkflowStepTemplate Role(string code, string name, A action, string role, int[] returnTo, bool edit = false, bool optional = false, int? sla = 24) =>
        new(code, name, action, T.Role, role, null, edit, optional, returnTo, null, sla);

    private static WorkflowStepTemplate Supplier(string code, string name, A action, int[] returnTo, int? sla = 48) =>
        new(code, name, action, T.DocumentSupplier, null, null, action == A.SupplierReview, false, returnTo, null, sla);

    private static WorkflowStepTemplate Creator(string code, string name, A action, int[] returnTo) =>
        new(code, name, action, T.Creator, null, null, false, false, returnTo, null, null);

    private static IEnumerable<(string Code, string Name, DocumentType Type, WorkflowStepTemplate[] Steps)> Definitions()
    {
        // Contract (spec §6.1 B3): review → supplier review → (accountant reviews supplier changes) → supplier signs
        // → accountant initials → director signs → clerk seals.
        WorkflowStepTemplate[] Contract() =>
        [
            Role("REVIEW", "Tổ rà soát kiểm tra", A.Review, RoleCatalog.Reviewer, [0], edit: true),
            Supplier("SUPPLIER_REVIEW", "Nhà cung cấp rà soát", A.SupplierReview, [0]),
            Role("ACCOUNTANT_REVIEW", "Kế toán xem thay đổi của NCC", A.Review, RoleCatalog.Accountant, [0, 2], edit: true, optional: true),
            Supplier("SUPPLIER", "Nhà cung cấp ký số", A.SupplierSign, [0]),
            Role("ACCOUNTANT_INITIAL", "Kế toán ký nháy", A.Initial, RoleCatalog.Accountant, [0]),
            Role("DIRECTOR", "Giám đốc ký số", A.Sign, RoleCatalog.Director, [0, 5]),
            Role("CLERK_SEAL", "Văn thư đóng dấu điện tử", A.OrgSeal, RoleCatalog.Clerk, [6], sla: 8),
        ];

        // Warehouse receipt (spec §6.4 B2): five signatures.
        WorkflowStepTemplate[] Receipt() =>
        [
            Role("STOREKEEPER", "Thủ kho ký nhận", A.Sign, RoleCatalog.Storekeeper, [0]),
            Supplier("DELIVERER", "Người giao hàng (NCC) ký", A.SupplierSign, [1]),
            Role("WAREHOUSE_ACCOUNTANT", "Kế toán kho ký", A.Sign, RoleCatalog.WarehouseAccountant, [0, 1]),
            Role("DEPT_HEAD", "Trưởng khoa ký", A.Sign, RoleCatalog.DeptHead, [0, 1]),
            Role("CHIEF_ACCOUNTANT", "Kế toán trưởng ký", A.Sign, RoleCatalog.ChiefAccountant, [0, 1]),
        ];

        // Payment minutes (spec §6.5): review → supplier → director → clerk seal.
        WorkflowStepTemplate[] Payment() =>
        [
            Role("REVIEW", "Tổ rà soát kiểm tra", A.Review, RoleCatalog.Reviewer, [0], edit: true),
            Supplier("SUPPLIER", "Nhà cung cấp ký số", A.SupplierSign, [0]),
            Role("DIRECTOR", "Giám đốc ký số", A.Sign, RoleCatalog.Director, [0]),
            Role("CLERK_SEAL", "Văn thư đóng dấu điện tử", A.OrgSeal, RoleCatalog.Clerk, [3], sla: 8),
        ];

        yield return ("WF_CONTRACT", "Ký hợp đồng", DocumentType.Contract, Contract());
        yield return ("WF_APPENDIX", "Ký phụ lục hợp đồng", DocumentType.Appendix, Contract());
        yield return ("WF_PROPOSAL", "Ký phiếu đề xuất", DocumentType.Proposal,
            [Creator("CREATOR", "Người lập ký đề xuất", A.Sign, [])]);
        yield return ("WF_PROPOSAL_SUMMARY", "Ký duyệt tổng hợp đề xuất", DocumentType.ProposalSummary,
        [
            Creator("AGGREGATOR", "Người tổng hợp ký", A.Sign, []),
            Role("DOMAIN_HEAD", "Tổ trưởng duyệt (được sửa SL duyệt)", A.Approve, RoleCatalog.DomainHead, [0], edit: true),
            Role("DIRECTOR", "Giám đốc ký số", A.Sign, RoleCatalog.Director, [0, 2]),
        ]);
        yield return ("WF_RECEIPT", "Ký phiếu nhập kho / hoàn trả", DocumentType.WarehouseReceipt, Receipt());
        yield return ("WF_ACCEPTANCE", "Ký biên bản nghiệm thu", DocumentType.Acceptance, Payment());
        yield return ("WF_VALUATION", "Ký biên bản xác định giá trị (08a)", DocumentType.Valuation, Payment());
        yield return ("WF_PAYMENT_REQUEST", "Ký đề nghị thanh toán", DocumentType.PaymentRequest,
        [
            Supplier("SUPPLIER", "Nhà cung cấp ký số", A.SupplierSign, [0]),
            Role("DIRECTOR", "Giám đốc ký duyệt", A.Sign, RoleCatalog.Director, [0]),
        ]);
        yield return ("WF_LIQUIDATION", "Ký biên bản thanh lý", DocumentType.Liquidation, Payment());

        // Sandbox: lets administrators try the 5-signature receipt flow on a sample document (Phase 3 demo).
        yield return ("WF_SAMPLE", "Thử nghiệm: 5 chữ ký phiếu nhập kho", DocumentType.TemplateSample, Receipt());
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        var existing = await db.WorkflowTemplates.IgnoreQueryFilters().Select(t => t.Code).ToListAsync(ct);
        var created = 0;
        foreach (var (code, name, type, steps) in Definitions().Where(d => !existing.Contains(d.Code)))
        {
            var template = new WorkflowTemplate(code, name, type, null, isDefault: true);
            template.SetSteps(steps);
            db.WorkflowTemplates.Add(template);
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} workflow templates", created);
        }
    }
}
