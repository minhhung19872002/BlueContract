using BlueContract.Application.Bids;
using BlueContract.Application.Common;
using BlueContract.Application.Contracts;
using BlueContract.Application.Integration;
using BlueContract.Application.Proposals;
using BlueContract.Application.Receipts;
using BlueContract.Application.Signing;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Receipts;
using BlueContract.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlueContract.Infrastructure.Persistence.Seeding;

/// <summary>
/// Demo business data in several states (spec §12), produced by running the real use cases as the demo users, so every
/// document, workflow event and signature is exactly what users would create: 2 bid decisions (thuốc, VTYT) synced from
/// the mock HIS, a contract waiting for the supplier's review, a fully signed effective contract and a non-bidding repair
/// contract in draft. Idempotent: skipped once the first bid decision exists.
/// </summary>
internal sealed class DemoProcurementSeeder(IServiceProvider services, IHttpClientFactory http, ILogger<DemoProcurementSeeder> logger)
{
    public const string DrugBid = "125/QĐ-BVĐK";
    public const string SupplyBid = "126/QĐ-BVĐK";
    public const string ContractUnderReview = "15/2026/HĐ-BVĐK";
    public const string ContractEffective = "16/2026/HĐ-BVĐK";
    public const string ContractRepair = "17/2026/HĐ-SC";

    public async Task SeedAsync(CancellationToken ct)
    {
        bool contractsSeeded, proposalsSeeded, receiptsSeeded;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            contractsSeeded = await db.BidDecisions.AnyAsync(b => b.Number == DrugBid, ct);
            proposalsSeeded = await db.Proposals.AnyAsync(ct);
            receiptsSeeded = await db.WarehouseReceipts.AnyAsync(ct);
        }

        if (contractsSeeded && proposalsSeeded && receiptsSeeded)
        {
            return;
        }

        // Signing renders PDFs through Gotenberg: wait for it so the scenario is never left half-done.
        if (!await GotenbergReadyAsync(ct))
        {
            logger.LogWarning("Gotenberg is not reachable; demo procurement scenarios will be seeded on a later start");
            return;
        }

        if (!contractsSeeded)
        {
            await SeedContractsAsync(ct);
        }

        if (!proposalsSeeded)
        {
            await SeedProposalsAsync(ct);
        }

        if (!receiptsSeeded)
        {
            await SeedReceiptsAsync(ct);
        }

        logger.LogInformation("Demo procurement scenarios seeded");
    }

    private async Task SeedContractsAsync(CancellationToken ct)
    {
        var drugBid = await AsAsync("muasam", sp => CreateBidAsync(sp, DrugBid, "THUOC", "0101234567",
            "Gói thầu số 01: Cung cấp thuốc generic năm 2026", ct), ct);
        var supplyBid = await AsAsync("muasam", sp => CreateBidAsync(sp, SupplyBid, "VTYT", "0302345678",
            "Gói thầu số 02: Cung cấp vật tư y tế tiêu hao năm 2026", ct), ct);

        // HĐ thuốc: đã qua Tổ rà soát, đang chờ NCC1 rà soát trong menu Hợp đồng.
        var underReview = await AsAsync("muasam", sp => CreateBidContractAsync(sp, drugBid, ContractUnderReview, "Cung cấp thuốc generic năm 2026", "HD_THUOC", ct), ct);
        await AsAsync("muasam", sp => Ok(sp.GetRequiredService<ContractService>().SubmitAsync(underReview.ContractId, ct)), ct);
        await AsAsync("rasoat", sp => ApproveAsync(sp, underReview.DocumentId, "Nội dung phù hợp quyết định thầu, chuyển NCC rà soát", ct), ct);

        // HĐ VTYT: ký đủ 4 chữ ký → có hiệu lực.
        var effective = await AsAsync("muasam", sp => CreateBidContractAsync(sp, supplyBid, ContractEffective, "Cung cấp vật tư y tế tiêu hao năm 2026", "HD_VTYT", ct), ct);
        await AsAsync("muasam", sp => Ok(sp.GetRequiredService<ContractService>().SubmitAsync(effective.ContractId, ct)), ct);
        await AsAsync("rasoat", sp => ApproveAsync(sp, effective.DocumentId, "Đã rà soát", ct), ct);
        await AsAsync("ncc2", sp => Ok(sp.GetRequiredService<ContractService>().SupplierRespondAsync(effective.ContractId, new SupplierResponseRequest(true, null), ct)), ct);
        foreach (var signer in new[] { "ncc2", "ketoan", "giamdoc", "vanthu" })
        {
            await AsAsync(signer, sp => SignAsync(sp, effective.DocumentId, ct), ct);
        }

        // HĐ sửa chữa ngoài thầu (bản nháp).
        await AsAsync("muasam", sp => CreateRepairContractAsync(sp, ct), ct);
    }

    /// <summary>
    /// Đề xuất / tổng hợp / gọi hàng on the effective VTYT contract: one summary waiting for the domain head ("đề xuất
    /// chờ duyệt") and one approved summary with a call order waiting for the supplier ("gọi hàng chờ xác nhận").
    /// </summary>
    private async Task SeedProposalsAsync(CancellationToken ct)
    {
        var (department, warehouse, items) = await AsAsync("khoanoi", async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var dep = await db.Departments.Where(d => d.Code == "KNOI").Select(d => d.Id).FirstAsync(ct);
            var wh = await db.Warehouses.Where(w => w.Code == "KHO_VTYT").Select(w => w.Id).FirstAsync(ct);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var candidates = Value(await sp.GetRequiredService<ProposalService>().CandidatesAsync(ProcurementDomain.MedicalSupply, wh,
                today.AddDays(-30), today, dep, ct));
            return (dep, wh, candidates.Items.Take(5).ToList());
        }, ct);
        if (items.Count < 5)
        {
            return; // no effective call-off contract in the domain
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        async Task<Guid> ProposeAsync(IEnumerable<ProposalCandidateDto> lines) =>
            await AsAsync("khoanoi", async sp =>
            {
                var proposals = sp.GetRequiredService<ProposalService>();
                var id = Value(await proposals.CreateAsync(new SaveProposalRequest(department, warehouse, ProcurementDomain.MedicalSupply,
                    today.AddDays(-30), today, "Đề xuất bổ sung cơ số vật tư tháng này",
                    [.. lines.Select(l => new ProposalLineRequest(l.ContractItemId, Math.Max(1, decimal.Round(l.ContractRemaining * 0.05m, 0)), null))]), ct));
                await Ok(proposals.SubmitAsync(id, ct));
                var documentId = await sp.GetRequiredService<AppDbContext>().Proposals.Where(p => p.Id == id).Select(p => p.DocumentId!.Value).FirstAsync(ct);
                await SignAsync(sp, documentId, ct);
                return id;
            }, ct);

        var pending = await ProposeAsync(items.Take(3));
        var forCall = await ProposeAsync(items.Skip(3));

        async Task<(Guid Id, Guid DocumentId)> SummarizeAsync(Guid proposalId, string period) =>
            await AsAsync("tonghopvt", async sp =>
            {
                var summaries = sp.GetRequiredService<ProposalSummaryService>();
                var id = Value(await summaries.CreateAsync(new SaveSummaryRequest(ProcurementDomain.MedicalSupply, null, period, null, [proposalId]), ct));
                await Ok(summaries.SubmitAsync(id, ct));
                var documentId = await sp.GetRequiredService<AppDbContext>().ProposalSummaries.Where(s => s.Id == id).Select(s => s.DocumentId!.Value).FirstAsync(ct);
                await SignAsync(sp, documentId, ct);
                return (id, documentId);
            }, ct);

        await SummarizeAsync(pending, $"Đợt 2 tháng {today:MM/yyyy}");
        var approved = await SummarizeAsync(forCall, $"Đợt 1 tháng {today:MM/yyyy}");
        await AsAsync("totruongvattu", sp => ApproveAsync(sp, approved.DocumentId, "Đồng ý số lượng đề xuất", ct), ct);
        await AsAsync("giamdoc", sp => SignAsync(sp, approved.DocumentId, ct), ct);
        await AsAsync("tonghopvt", async sp =>
        {
            var summary = Value(await sp.GetRequiredService<ProposalSummaryService>().GetAsync(approved.Id, ct));
            Value(await sp.GetRequiredService<CallOrderService>().CreateAsync(new CreateCallsRequest(summary.Id, warehouse, today.AddDays(5), "Mai Văn Tú",
                "Giao hàng giờ hành chính, liên hệ thủ kho trước khi giao", [.. summary.Lines.Select(l => new CallLineRequest(l.Id, l.ApprovedQuantity))]), ct));
        }, ct);
    }

    /// <summary>
    /// Phiếu nhập kho từ HIS on the effective VTYT contract: one fully signed by the 5 signers (ready for acceptance), one
    /// with warnings waiting for the storekeeper ("phiếu nhập kho có cảnh báo") and one Invalid (item outside the contract).
    /// </summary>
    private async Task SeedReceiptsAsync(CancellationToken ct)
    {
        var contractId = await AsSystemAsync(sp => sp.GetRequiredService<AppDbContext>().Contracts
            .Where(c => c.Number == ContractEffective && c.Status == ContractStatus.Effective).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct), ct);
        if (contractId is null)
        {
            return;
        }

        async Task<HisIngestResultDto> SimulateAsync(SimulationScenario scenario, int lines) =>
            await AsSystemAsync(async sp => Value(await sp.GetRequiredService<HisSimulator>().SimulateAsync(
                new SimulateReceiptRequest(contractId, ReceiptType.Receipt, scenario, SimulationMode.Push, true, lines), ct)).Result!, ct);

        // 1. HIS đẩy phiếu hợp lệ (theo đơn gọi hàng còn mở) → ký đủ 5 chữ ký; người ký ghi lý do nếu phiếu có cảnh báo.
        var signed = await SimulateAsync(SimulationScenario.Valid, 3);
        var documentId = await AsSystemAsync(sp => sp.GetRequiredService<AppDbContext>().WarehouseReceipts.Where(r => r.Id == signed.ReceiptId)
            .Select(r => r.DocumentId!.Value).FirstAsync(ct), ct);
        foreach (var signer in new[] { "thukho", "ncc2", "ketoankho", "truongkhoa", "ketoantruong" })
        {
            await AsAsync(signer, sp => SignAsync(sp, documentId, ct, "Đã kiểm tra thực tế hàng nhập, đồng ý"), ct);
        }

        // 2. Phiếu có cảnh báo (đơn giá thấp hơn HĐ, hạn dùng gần, chưa có hóa đơn) chờ thủ kho; 3. phiếu không hợp lệ.
        await SimulateAsync(SimulationScenario.Warning, 2);
        await SimulateAsync(SimulationScenario.Invalid, 1);
    }
    /// <summary>Runs without a signed-in user, like a call from the HIS.</summary>
    private async Task<T> AsSystemAsync<T>(Func<IServiceProvider, Task<T>> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private async Task<bool> GotenbergReadyAsync(CancellationToken ct)
    {
        var client = http.CreateClient(DependencyInjection.GotenbergClient);
        for (var attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), ct);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // not up yet
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        return false;
    }

    private async Task<T> AsAsync<T>(string userName, Func<IServiceProvider, Task<T>> action, CancellationToken ct)
    {
        ImpersonatedUser user;
        using (var lookup = services.CreateScope())
        {
            user = await ImpersonatedUser.LoadAsync(lookup.ServiceProvider.GetRequiredService<AppDbContext>(), userName, ct);
        }

        using (UserContextOverride.Use(user))
        {
            using var scope = services.CreateScope();
            return await action(scope.ServiceProvider);
        }
    }

    private async Task AsAsync(string userName, Func<IServiceProvider, Task> action, CancellationToken ct) =>
        await AsAsync(userName, async sp =>
        {
            await action(sp);
            return true;
        }, ct);

    private static async Task<Guid> CreateBidAsync(IServiceProvider sp, string number, string category, string taxCode, string package, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var bids = sp.GetRequiredService<BidDecisionService>();
        var categoryId = await db.ItemCategories.Where(c => c.Code == category).Select(c => c.Id).FirstAsync(ct);
        var supplierId = await db.Suppliers.Where(s => s.TaxCode == taxCode).Select(s => s.Id).FirstAsync(ct);
        var id = Value(await bids.CreateAsync(new SaveBidDecisionRequest(number, new DateOnly(2026, 2, 15),
            "Phê duyệt kết quả lựa chọn nhà thầu " + package.ToLowerInvariant(), package, "Đấu thầu rộng rãi qua mạng",
            "Nguồn thu sự nghiệp và quỹ bảo hiểm y tế", "12 tháng kể từ ngày hợp đồng có hiệu lực", categoryId, null, new DateOnly(2027, 2, 28)), ct));
        await Ok(bids.UpsertSupplierAsync(id, new BidSupplierRequest(supplierId, 0), ct));
        Value(await bids.SyncFromHisAsync(id, null, ct));
        var items = Value(await bids.ItemsAsync(id, null, ct));
        await Ok(bids.UpsertSupplierAsync(id, new BidSupplierRequest(supplierId, items.Sum(i => i.Amount)), ct));
        await Ok(bids.ActivateAsync(id, ct));
        return id;
    }

    private static async Task<(Guid ContractId, Guid DocumentId)> CreateBidContractAsync(IServiceProvider sp, Guid bidId, string number, string name, string template, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var bid = await db.BidDecisions.AsNoTracking().FirstAsync(b => b.Id == bidId, ct);
        var items = Value(await sp.GetRequiredService<BidDecisionService>().ItemsAsync(bidId, null, ct));
        var templateId = await db.DocumentTemplates.Where(t => t.Code == template).Select(t => t.Id).FirstAsync(ct);
        var contracts = sp.GetRequiredService<ContractService>();
        var id = Value(await contracts.CreateAsync(new SaveContractRequest(
            ProcurementType.Bidding, bidId, items[0].SupplierId, null, number, name, ContractKind.UnitPriceCallOff, true, bid.CategoryId, null, null,
            null, new DateOnly(2026, 3, 1), new DateOnly(2027, 2, 28), "12 tháng kể từ ngày hợp đồng có hiệu lực",
            "Thanh toán bằng chuyển khoản theo từng đợt giao hàng, trong vòng 90 ngày kể từ ngày nghiệm thu và nhận đủ hồ sơ thanh toán hợp lệ.",
            "Hàng hóa còn hạn dùng tối thiểu 12 tháng tại thời điểm giao hàng; đổi trả hàng lỗi trong 07 ngày.",
            "Giao hàng tại kho của Bên A trong vòng 05 ngày làm việc kể từ ngày nhận đơn gọi hàng.",
            "BL-" + number.Split('/')[0] + "/2026", "Ngân hàng TMCP Đầu tư và Phát triển Việt Nam - CN Nghệ An", null, new DateOnly(2027, 3, 31), templateId,
            [.. items.Take(10).Select(i => new ContractLineRequest(i.ItemId, null, decimal.Round(i.Quantity * 0.6m, 0), i.UnitPrice, 5))]), ct));
        var documentId = await db.Contracts.Where(c => c.Id == id).Select(c => c.DocumentId!.Value).FirstAsync(ct);
        return (id, documentId);
    }

    private static async Task<Guid> CreateRepairContractAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var categoryId = await db.ItemCategories.Where(c => c.Code == "SUA_CHUA").Select(c => c.Id).FirstAsync(ct);
        var supplierId = await db.Suppliers.Where(s => s.TaxCode == "2901234567").Select(s => s.Id).FirstAsync(ct);
        var templateId = await db.DocumentTemplates.Where(t => t.Code == "HD_DICHVU_KHONGTHAU").Select(t => t.Id).FirstAsync(ct);
        return Value(await sp.GetRequiredService<ContractService>().CreateAsync(new SaveContractRequest(
            ProcurementType.NonBidding, null, supplierId, null, ContractRepair, "Sửa chữa hệ thống khí y tế Khoa Hồi sức tích cực", ContractKind.LumpSum, false,
            categoryId, null, "Hợp đồng ngoài thầu (giá trị dưới hạn mức chỉ định thầu rút gọn)", null, null, null, "45 ngày kể từ ngày ký",
            "Thanh toán một lần bằng chuyển khoản sau khi nghiệm thu, bàn giao.", "Bảo hành 12 tháng cho vật tư thay thế và công sửa chữa.",
            "Thi công tại Khoa Hồi sức tích cực - Chống độc, ngoài giờ cao điểm.", null, null, null, null, templateId,
            [
                new ContractLineRequest(null, new NewItemRequest("SC-KYT-01", "Thay van giảm áp oxy trung tâm", "Bộ", "Van giảm áp 2 cấp, đồng hồ báo áp", null, "Amico", "Canada", null), 4, 9_800_000, 10),
                new ContractLineRequest(null, new NewItemRequest("SC-KYT-02", "Thay ổ cắm khí y tế đầu giường", "Cái", "Ổ cắm oxy/khí nén/hút chuẩn DIN", null, "Amico", "Canada", null), 12, 3_450_000, 10),
                new ContractLineRequest(null, new NewItemRequest("SC-KYT-03", "Nhân công kiểm tra rò rỉ, hiệu chỉnh hệ thống", "Gói", null, null, null, null, null), 1, 15_000_000, 10),
            ]), ct));
    }

    private static async Task ApproveAsync(IServiceProvider sp, Guid documentId, string comment, CancellationToken ct)
    {
        var wf = Value(await sp.GetRequiredService<WorkflowService>().GetForDocumentAsync(documentId, ct));
        var stepId = wf?.MyStepId ?? throw new InvalidOperationException("Demo seeding: the user has no active step.");
        Value(await sp.GetRequiredService<WorkflowService>().ApproveAsync(stepId, new ApproveStepRequest(comment, false), ct));
    }

    private static async Task SignAsync(IServiceProvider sp, Guid documentId, CancellationToken ct, string? reasonIfRequired = null)
    {
        var wf = Value(await sp.GetRequiredService<WorkflowService>().GetForDocumentAsync(documentId, ct));
        var stepId = wf?.MyStepId ?? throw new InvalidOperationException("Demo seeding: the user has no signing step.");
        var comment = wf.Gate?.ReasonRequired == true ? reasonIfRequired : null;
        Value(await sp.GetRequiredService<SigningService>().BeginAsync(stepId, new BeginSignRequest("MOCK", null, comment), ct));
    }

    private static T Value<T>(Result<T> result) =>
        result.IsSuccess ? result.Value : throw new InvalidOperationException($"Demo seeding failed: {result.Error!.Code}");

    private static async Task Ok(Task<Result> task)
    {
        var result = await task;
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Demo seeding failed: {result.Error!.Code}");
        }
    }
}
