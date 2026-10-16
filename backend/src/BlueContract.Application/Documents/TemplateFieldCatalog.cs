using System.Text.Json.Nodes;
using BlueContract.Domain.Documents;

namespace BlueContract.Application.Documents;

public sealed record TemplateField(string Path, string Description);

public sealed record TemplateCollection(string Name, string Description, IReadOnlyList<TemplateField> Fields);

public sealed record TemplateSchema(
    TemplateKind Kind,
    IReadOnlyList<TemplateField> Fields,
    IReadOnlyList<TemplateCollection> Collections,
    IReadOnlyList<string> SignatureAnchors);

/// <summary>
/// Placeholders each template kind may use ("danh sách placeholder hợp lệ", spec §9.1/14) and sample data for previews.
/// The data builders of each module (Phase 4+) must produce exactly these paths.
/// </summary>
public static class TemplateFieldCatalog
{
    private static readonly TemplateField[] OrganizationFields =
    [
        new("Organization.Name", "Tên bệnh viện"),
        new("Organization.ShortName", "Tên viết tắt"),
        new("Organization.TaxCode", "Mã số thuế / mã ĐVQHNS"),
        new("Organization.Address", "Địa chỉ"),
        new("Organization.Phone", "Điện thoại"),
        new("Organization.RepresentativeName", "Người đại diện"),
        new("Organization.RepresentativeTitle", "Chức vụ người đại diện"),
        new("Organization.BankAccountNumber", "Số tài khoản"),
        new("Organization.BankName", "Mở tại ngân hàng"),
        new("Organization.TreasuryAccountNumber", "Tài khoản Kho bạc"),
        new("Organization.TreasuryName", "Kho bạc Nhà nước"),
    ];

    private static readonly TemplateField[] SupplierFields =
    [
        new("Supplier.Name", "Tên nhà cung cấp"),
        new("Supplier.TaxCode", "Mã số thuế NCC"),
        new("Supplier.Address", "Địa chỉ NCC"),
        new("Supplier.Phone", "Điện thoại NCC"),
        new("Supplier.Email", "Email NCC"),
        new("Supplier.RepresentativeName", "Người đại diện NCC"),
        new("Supplier.RepresentativeTitle", "Chức vụ"),
        new("Supplier.AuthorizationDocument", "Giấy ủy quyền"),
        new("Supplier.BankAccountNumber", "Số tài khoản NCC"),
        new("Supplier.BankName", "Ngân hàng NCC"),
    ];

    private static readonly TemplateField[] ContractFields =
    [
        new("Contract.Number", "Số hợp đồng"),
        new("Contract.Name", "Tên hợp đồng"),
        new("Contract.SignedDate", "Ngày ký (dùng | date hoặc | datetext)"),
        new("Contract.EffectiveDate", "Ngày hiệu lực"),
        new("Contract.ExpiryDate", "Ngày hết hạn"),
        new("Contract.Duration", "Thời gian thực hiện hợp đồng"),
        new("Contract.ProcurementTypeText", "Hình thức: Trong thầu / Ngoài thầu"),
        new("Contract.IsBidding", "Là hợp đồng trong thầu (dùng với #if)"),
        new("Contract.CategoryText", "Hạng mục (Thuốc, VTYT...)"),
        new("Contract.FacilityName", "Cơ sở thực hiện"),
        new("Contract.AmountBeforeVat", "Giá trị trước thuế"),
        new("Contract.VatAmount", "Thuế GTGT"),
        new("Contract.TotalAmount", "Tổng giá trị hợp đồng"),
        new("Contract.PaymentTerms", "Điều khoản thanh toán"),
        new("Contract.WarrantyTerms", "Điều khoản bảo hành"),
        new("Contract.DeliveryTerms", "Điều khoản giao hàng"),
        new("Contract.GuaranteeText", "Bảo lãnh thực hiện hợp đồng"),
        new("Contract.FundingSource", "Nguồn vốn"),
        new("BidDecision.Number", "Số quyết định phê duyệt KQLCNT"),
        new("BidDecision.Date", "Ngày quyết định"),
        new("BidDecision.PackageName", "Tên gói thầu"),
        new("BidDecision.SelectionMethod", "Hình thức lựa chọn nhà thầu"),
    ];

    private static readonly TemplateField[] DocumentFields =
    [
        new("Document.Number", "Số văn bản"),
        new("Document.Date", "Ngày văn bản"),
        new("Document.Place", "Địa danh (VD: Nghệ An)"),
        new("Today", "Ngày hiện tại"),
    ];

    private static readonly TemplateCollection ContractItems = new("Items", "Danh mục hàng hóa của hợp đồng",
    [
        new("Code", "Mã hàng"),
        new("Name", "Tên hàng hóa / dịch vụ"),
        new("ActiveIngredient", "Hoạt chất / thông số kỹ thuật"),
        new("Specification", "Quy cách đóng gói"),
        new("Unit", "Đơn vị tính"),
        new("Manufacturer", "Hãng sản xuất"),
        new("Country", "Nước sản xuất"),
        new("RegistrationNumber", "Số đăng ký / GPNK"),
        new("BidGroup", "Nhóm thầu"),
        new("Quantity", "Số lượng"),
        new("UnitPrice", "Đơn giá"),
        new("VatRate", "Thuế suất GTGT (%)"),
        new("Amount", "Thành tiền"),
    ]);

    private static readonly string[] ContractSignatures = ["SIG_SUPPLIER", "SIG_ACCOUNTANT_INITIAL", "SIG_DIRECTOR", "SIG_CLERK_SEAL"];

    public static IReadOnlyDictionary<TemplateKind, TemplateSchema> Schemas { get; } = Build();

    public static TemplateSchema For(TemplateKind kind) => Schemas[kind];

    private static Dictionary<TemplateKind, TemplateSchema> Build()
    {
        var common = OrganizationFields.Concat(SupplierFields).Concat(DocumentFields).ToArray();
        var withContract = common.Concat(ContractFields).ToArray();
        return new Dictionary<TemplateKind, TemplateSchema>
        {
            [TemplateKind.Contract] = new(TemplateKind.Contract, withContract, [ContractItems], ContractSignatures),
            [TemplateKind.Appendix] = new(TemplateKind.Appendix,
                [.. withContract,
                 new("Appendix.Number", "Số phụ lục"),
                 new("Appendix.Date", "Ngày phụ lục"),
                 new("Appendix.TypeText", "Loại phụ lục (bổ sung số lượng / giảm giá / thay đổi thông tin / gia hạn)"),
                 new("Appendix.Reason", "Lý do điều chỉnh"),
                 new("Appendix.NewExpiryDate", "Ngày hết hạn mới (gia hạn)"),
                 new("Appendix.ValueChange", "Giá trị tăng/giảm"),
                 new("Appendix.NewTotalAmount", "Giá trị hợp đồng sau điều chỉnh")],
                [new("Changes", "Nội dung thay đổi", [new("ItemName", "Mặt hàng / nội dung"), new("Field", "Thông tin thay đổi"), new("OldValue", "Giá trị cũ"), new("NewValue", "Giá trị mới")])],
                ContractSignatures),
            [TemplateKind.Proposal] = new(TemplateKind.Proposal,
                [.. common,
                 new("Proposal.Number", "Số phiếu đề xuất"),
                 new("Proposal.Date", "Ngày lập"),
                 new("Proposal.Period", "Kỳ đề xuất"),
                 new("Proposal.DepartmentName", "Khoa/phòng đề xuất"),
                 new("Proposal.FacilityName", "Cơ sở"),
                 new("Proposal.DomainText", "Mảng (Thuốc/VTYT...)"),
                 new("Proposal.CreatedByName", "Người lập"),
                 new("Proposal.Note", "Ghi chú")],
                [new("Lines", "Mặt hàng đề xuất",
                [
                    new("ItemName", "Tên hàng"), new("Unit", "ĐVT"), new("ContractNumber", "Số HĐ"), new("OpeningStock", "Tồn đầu kỳ"),
                    new("Received", "Nhập trong kỳ"), new("Issued", "Xuất trong kỳ"), new("CurrentStock", "Tồn hiện tại"),
                    new("ContractRemaining", "SL còn lại HĐ"), new("ProposedQuantity", "SL đề xuất"), new("Note", "Ghi chú"),
                ])],
                ["SIG_CREATOR", "SIG_DEPT_HEAD"]),
            [TemplateKind.ProposalSummary] = new(TemplateKind.ProposalSummary,
                [.. common,
                 new("Summary.Number", "Số phiếu tổng hợp"),
                 new("Summary.Date", "Ngày lập"),
                 new("Summary.Period", "Kỳ"),
                 new("Summary.DomainText", "Mảng"),
                 new("Summary.AggregatorName", "Người tổng hợp"),
                 new("Summary.ProposalCount", "Số phiếu đề xuất gộp")],
                [new("Lines", "Mặt hàng tổng hợp",
                [
                    new("ItemName", "Tên hàng"), new("Unit", "ĐVT"), new("SupplierName", "Nhà cung cấp"), new("ContractNumber", "Số HĐ"),
                    new("ProposedQuantity", "Tổng SL đề xuất"), new("OpeningStock", "Tồn đầu"), new("Received", "Nhập"), new("Issued", "Xuất"),
                    new("CurrentStock", "Tồn"), new("ApprovedQuantity", "SL duyệt"), new("UnitPrice", "Đơn giá"), new("Amount", "Thành tiền"),
                ])],
                ["SIG_AGGREGATOR", "SIG_DOMAIN_HEAD", "SIG_DIRECTOR"]),
            [TemplateKind.CallOrder] = new(TemplateKind.CallOrder,
                [.. withContract,
                 new("CallOrder.Number", "Số đơn gọi hàng"),
                 new("CallOrder.Date", "Ngày gọi"),
                 new("CallOrder.DeliveryDeadline", "Hạn giao"),
                 new("CallOrder.WarehouseName", "Kho nhận"),
                 new("CallOrder.WarehouseAddress", "Địa chỉ kho"),
                 new("CallOrder.SummaryNumber", "Số phiếu tổng hợp"),
                 new("CallOrder.TotalAmount", "Tổng tiền"),
                 new("CallOrder.ContactName", "Người liên hệ")],
                [new("Lines", "Mặt hàng gọi", [new("ItemName", "Tên hàng"), new("Unit", "ĐVT"), new("Quantity", "SL gọi"), new("UnitPrice", "Đơn giá"), new("Amount", "Thành tiền")])],
                ["SIG_AGGREGATOR"]),
            [TemplateKind.WarehouseReceipt] = new(TemplateKind.WarehouseReceipt,
                [.. common,
                 new("Receipt.Number", "Số phiếu"),
                 new("Receipt.Date", "Ngày phiếu"),
                 new("Receipt.TypeText", "PHIẾU NHẬP KHO / PHIẾU HOÀN TRẢ"),
                 new("Receipt.IsReturn", "Là phiếu hoàn trả (dùng với #if)"),
                 new("Receipt.IsVirtual", "Là phiếu xác nhận khối lượng không qua kho"),
                 new("Receipt.WarehouseName", "Kho"),
                 new("Receipt.DelivererName", "Người giao hàng"),
                 new("Receipt.BidDecisionNumber", "Số QĐ thầu"),
                 new("Receipt.ContractNumber", "Số hợp đồng"),
                 new("Receipt.InvoiceSymbol", "Ký hiệu hóa đơn"),
                 new("Receipt.InvoiceNumber", "Số hóa đơn"),
                 new("Receipt.InvoiceDate", "Ngày hóa đơn"),
                 new("Receipt.AmountBeforeVat", "Tiền hàng"),
                 new("Receipt.VatAmount", "Tiền thuế"),
                 new("Receipt.TotalAmount", "Tổng tiền")],
                [new("Lines", "Chi tiết phiếu",
                [
                    new("ItemCode", "Mã hàng"), new("ItemName", "Tên hàng"), new("Unit", "ĐVT"), new("LotNumber", "Số lô"), new("ExpiryDate", "Hạn dùng"),
                    new("Quantity", "Số lượng"), new("UnitPrice", "Đơn giá"), new("Amount", "Thành tiền"),
                ])],
                ["SIG_STOREKEEPER", "SIG_DELIVERER", "SIG_WAREHOUSE_ACCOUNTANT", "SIG_DEPT_HEAD", "SIG_CHIEF_ACCOUNTANT"]),
            [TemplateKind.Acceptance] = new(TemplateKind.Acceptance,
                [.. withContract,
                 new("Acceptance.Number", "Số biên bản nghiệm thu"),
                 new("Acceptance.Date", "Ngày nghiệm thu"),
                 new("Acceptance.PeriodText", "Kỳ nghiệm thu"),
                 new("Acceptance.TotalAmount", "Giá trị nghiệm thu"),
                 new("Acceptance.Conclusion", "Kết luận")],
                [
                    new("Receipts", "Phiếu nhập kho được nghiệm thu", [new("Number", "Số phiếu"), new("Date", "Ngày"), new("InvoiceNumber", "Số hóa đơn"), new("Amount", "Giá trị")]),
                    new("Items", "Tổng hợp hàng hóa", [new("Name", "Tên hàng"), new("Unit", "ĐVT"), new("Quantity", "Số lượng"), new("UnitPrice", "Đơn giá"), new("Amount", "Thành tiền")]),
                ],
                ["SIG_SUPPLIER", "SIG_DIRECTOR", "SIG_CLERK_SEAL"]),
            [TemplateKind.Valuation] = new(TemplateKind.Valuation,
                [.. withContract,
                 new("Valuation.Number", "Số biên bản XĐGT (Mẫu 08a)"),
                 new("Valuation.Date", "Ngày lập"),
                 new("Valuation.PeriodText", "Kỳ thanh toán"),
                 new("Valuation.PaymentNumber", "Lần thanh toán thứ"),
                 new("Valuation.ContractValue", "Giá trị hợp đồng"),
                 new("Valuation.AdvanceAmount", "Giá trị tạm ứng theo hợp đồng"),
                 new("Valuation.PreviousCumulative", "Lũy kế giá trị thực hiện đến cuối kỳ trước"),
                 new("Valuation.CurrentPeriodValue", "Giá trị thực hiện kỳ này"),
                 new("Valuation.CumulativeToDate", "Lũy kế đến hết kỳ này"),
                 new("Valuation.CompletionPercent", "% so với giá trị hợp đồng"),
                 new("Valuation.AdvanceRecovered", "Thu hồi tạm ứng"),
                 new("Valuation.RequestedAmount", "Giá trị đề nghị thanh toán kỳ này")],
                [new("Items", "Khối lượng hoàn thành",
                [
                    new("Name", "Tên công việc / hàng hóa"), new("Unit", "ĐVT"), new("ContractQuantity", "KL theo HĐ"), new("UnitPrice", "Đơn giá"),
                    new("PreviousQuantity", "KL lũy kế đến cuối kỳ trước"), new("CurrentQuantity", "KL thực hiện kỳ này"), new("CumulativeQuantity", "KL lũy kế đến hết kỳ này"),
                    new("ContractAmount", "Thành tiền theo HĐ"), new("PreviousAmount", "Lũy kế đến cuối kỳ trước"), new("CurrentAmount", "Thực hiện kỳ này"),
                    new("CumulativeAmount", "Lũy kế đến hết kỳ này"),
                ])],
                ["SIG_SUPPLIER", "SIG_DIRECTOR", "SIG_CLERK_SEAL"]),
            [TemplateKind.PaymentRequest] = new(TemplateKind.PaymentRequest,
                [.. withContract,
                 new("Payment.Number", "Số đề nghị thanh toán"),
                 new("Payment.Date", "Ngày đề nghị"),
                 new("Payment.ValuationNumber", "Số biên bản XĐGT"),
                 new("Payment.RequestedAmount", "Số tiền đề nghị thanh toán"),
                 new("Payment.PaidBefore", "Đã thanh toán các kỳ trước"),
                 new("Payment.AdvanceRecovered", "Thu hồi tạm ứng"),
                 new("Payment.PaymentOrderNumber", "Số ủy nhiệm chi")],
                [],
                ["SIG_SUPPLIER", "SIG_DIRECTOR"]),
            [TemplateKind.Liquidation] = new(TemplateKind.Liquidation,
                [.. withContract,
                 new("Liquidation.Number", "Số biên bản thanh lý"),
                 new("Liquidation.Date", "Ngày thanh lý"),
                 new("Liquidation.ContractValue", "Giá trị hợp đồng (kể cả phụ lục)"),
                 new("Liquidation.ExecutedValue", "Giá trị thực hiện"),
                 new("Liquidation.PaidAmount", "Đã thanh toán"),
                 new("Liquidation.RemainingAmount", "Còn phải thanh toán"),
                 new("Liquidation.ReductionAmount", "Giá trị giảm trừ / không thực hiện"),
                 new("Liquidation.Notes", "Các nội dung khác")],
                [new("Valuations", "Các lần xác định giá trị / thanh toán",
                    [new("Number", "Số biên bản"), new("Date", "Ngày"), new("Amount", "Giá trị"), new("PaidAmount", "Đã thanh toán"), new("PaymentOrderNumber", "Số UNC")])],
                ["SIG_SUPPLIER", "SIG_DIRECTOR", "SIG_CLERK_SEAL"]),
        };
    }

    /// <summary>Realistic sample data used for template previews and the "tài liệu thử" sandbox.</summary>
    public static JsonObject SampleData(TemplateKind kind)
    {
        var data = new JsonObject
        {
            ["Organization"] = new JsonObject
            {
                ["Name"] = "Bệnh viện Đa khoa Thành phố Vinh",
                ["ShortName"] = "BVĐK TP Vinh",
                ["TaxCode"] = "2900000001",
                ["Address"] = "Thành phố Vinh, tỉnh Nghệ An",
                ["Phone"] = "0238 3800 000",
                ["RepresentativeName"] = "Nguyễn Văn Minh",
                ["RepresentativeTitle"] = "Giám đốc",
                ["BankAccountNumber"] = "3711 0000 0001",
                ["BankName"] = "Kho bạc Nhà nước tỉnh Nghệ An",
                ["TreasuryAccountNumber"] = "9527.1.1000001",
                ["TreasuryName"] = "Kho bạc Nhà nước TP Vinh",
            },
            ["Supplier"] = new JsonObject
            {
                ["Name"] = "Công ty Cổ phần Dược phẩm Minh An",
                ["TaxCode"] = "0101234567",
                ["Address"] = "Số 1 phố Mẫu, quận Mẫu, Hà Nội",
                ["Phone"] = "0900 000 000",
                ["Email"] = "ncc1@example.vn",
                ["RepresentativeName"] = "Trần Thị Lan",
                ["RepresentativeTitle"] = "Giám đốc",
                ["AuthorizationDocument"] = "",
                ["BankAccountNumber"] = "0001 0002 0003",
                ["BankName"] = "Ngân hàng TMCP Ngoại thương Việt Nam",
            },
            ["Document"] = new JsonObject { ["Number"] = "01/2026", ["Date"] = "2026-10-16", ["Place"] = "Nghệ An" },
            ["Today"] = "2026-10-16",
            ["Contract"] = new JsonObject
            {
                ["Number"] = "15/2026/HĐ-BV",
                ["Name"] = "Cung cấp thuốc generic năm 2026",
                ["SignedDate"] = "2026-03-01",
                ["EffectiveDate"] = "2026-03-01",
                ["ExpiryDate"] = "2027-02-28",
                ["Duration"] = "12 tháng kể từ ngày hợp đồng có hiệu lực",
                ["ProcurementTypeText"] = "Trong thầu",
                ["IsBidding"] = true,
                ["CategoryText"] = "Thuốc",
                ["FacilityName"] = "Cơ sở 1",
                ["AmountBeforeVat"] = 1_116_694_444m,
                ["VatAmount"] = 88_335_556m,
                ["TotalAmount"] = 1_205_030_000m,
                ["PaymentTerms"] = "Thanh toán bằng chuyển khoản theo từng đợt giao hàng, trong vòng 90 ngày kể từ ngày nghiệm thu và nhận đủ hồ sơ thanh toán hợp lệ.",
                ["WarrantyTerms"] = "Hạn dùng còn lại tối thiểu 12 tháng tại thời điểm giao hàng.",
                ["DeliveryTerms"] = "Giao hàng tại kho của Bên A trong vòng 05 ngày kể từ ngày nhận đơn gọi hàng.",
                ["GuaranteeText"] = "Bảo lãnh thực hiện hợp đồng bằng 3% giá trị hợp đồng, hiệu lực đến khi hoàn thành nghĩa vụ hợp đồng.",
                ["FundingSource"] = "Nguồn thu sự nghiệp, quỹ bảo hiểm y tế",
            },
            ["BidDecision"] = new JsonObject
            {
                ["Number"] = "1234/QĐ-BV",
                ["Date"] = "2026-02-15",
                ["PackageName"] = "Gói thầu số 01: Thuốc generic",
                ["SelectionMethod"] = "Đấu thầu rộng rãi qua mạng",
            },
            ["Items"] = new JsonArray(
                new JsonObject
                {
                    ["Code"] = "TH001",
                    ["Name"] = "Paracetamol 500mg",
                    ["ActiveIngredient"] = "Paracetamol",
                    ["Specification"] = "Hộp 10 vỉ x 10 viên",
                    ["Unit"] = "Viên",
                    ["Manufacturer"] = "Công ty Dược A",
                    ["Country"] = "Việt Nam",
                    ["RegistrationNumber"] = "VD-12345-20",
                    ["BidGroup"] = "N4",
                    ["Quantity"] = 200_000,
                    ["UnitPrice"] = 450m,
                    ["VatRate"] = 5,
                    ["Amount"] = 90_000_000m,
                },
                new JsonObject
                {
                    ["Code"] = "TH002",
                    ["Name"] = "Amoxicillin 500mg",
                    ["ActiveIngredient"] = "Amoxicillin",
                    ["Specification"] = "Hộp 10 vỉ x 10 viên",
                    ["Unit"] = "Viên",
                    ["Manufacturer"] = "Công ty Dược B",
                    ["Country"] = "Việt Nam",
                    ["RegistrationNumber"] = "VD-23456-21",
                    ["BidGroup"] = "N4",
                    ["Quantity"] = 150_000,
                    ["UnitPrice"] = 1_150m,
                    ["VatRate"] = 5,
                    ["Amount"] = 172_500_000m,
                }),
        };

        switch (kind)
        {
            case TemplateKind.Appendix:
                data["Appendix"] = new JsonObject
                {
                    ["Number"] = "01",
                    ["Date"] = "2026-08-01",
                    ["TypeText"] = "Bổ sung số lượng",
                    ["Reason"] = "Nhu cầu sử dụng tăng",
                    ["NewExpiryDate"] = "2027-02-28",
                    ["ValueChange"] = 22_500_000m,
                    ["NewTotalAmount"] = 1_227_530_000m,
                };
                data["Changes"] = new JsonArray(new JsonObject
                {
                    ["ItemName"] = "Paracetamol 500mg",
                    ["Field"] = "Số lượng",
                    ["OldValue"] = "200.000",
                    ["NewValue"] = "250.000",
                });
                break;
            case TemplateKind.Proposal:
                data["Proposal"] = new JsonObject
                {
                    ["Number"] = "DX-2026-0001",
                    ["Date"] = "2026-10-16",
                    ["Period"] = "Tháng 10/2026",
                    ["DepartmentName"] = "Khoa Nội tổng hợp",
                    ["FacilityName"] = "Cơ sở 1",
                    ["DomainText"] = "Thuốc",
                    ["CreatedByName"] = "Đặng Thị Thu",
                    ["Note"] = "",
                };
                data["Lines"] = new JsonArray(new JsonObject
                {
                    ["ItemName"] = "Paracetamol 500mg",
                    ["Unit"] = "Viên",
                    ["ContractNumber"] = "15/2026/HĐ-BV",
                    ["OpeningStock"] = 1_200,
                    ["Received"] = 5_000,
                    ["Issued"] = 4_800,
                    ["CurrentStock"] = 1_400,
                    ["ContractRemaining"] = 150_000,
                    ["ProposedQuantity"] = 6_000,
                    ["Note"] = "",
                });
                break;
            case TemplateKind.ProposalSummary:
                data["Summary"] = new JsonObject
                {
                    ["Number"] = "TH-2026-0001",
                    ["Date"] = "2026-10-18",
                    ["Period"] = "Tháng 10/2026",
                    ["DomainText"] = "Thuốc",
                    ["AggregatorName"] = "Võ Thị Mai",
                    ["ProposalCount"] = 5,
                };
                data["Lines"] = new JsonArray(new JsonObject
                {
                    ["ItemName"] = "Paracetamol 500mg",
                    ["Unit"] = "Viên",
                    ["SupplierName"] = "Công ty Cổ phần Dược phẩm Minh An",
                    ["ContractNumber"] = "15/2026/HĐ-BV",
                    ["ProposedQuantity"] = 24_000,
                    ["OpeningStock"] = 3_000,
                    ["Received"] = 20_000,
                    ["Issued"] = 18_500,
                    ["CurrentStock"] = 4_500,
                    ["ApprovedQuantity"] = 20_000,
                    ["UnitPrice"] = 450m,
                    ["Amount"] = 9_000_000m,
                });
                break;
            case TemplateKind.CallOrder:
                data["CallOrder"] = new JsonObject
                {
                    ["Number"] = "GH-2026-0001",
                    ["Date"] = "2026-10-20",
                    ["DeliveryDeadline"] = "2026-10-25",
                    ["WarehouseName"] = "Kho Thuốc chính",
                    ["WarehouseAddress"] = "Cơ sở 1",
                    ["SummaryNumber"] = "TH-2026-0001",
                    ["TotalAmount"] = 9_000_000m,
                    ["ContactName"] = "Mai Văn Tú",
                };
                data["Lines"] = new JsonArray(new JsonObject
                {
                    ["ItemName"] = "Paracetamol 500mg",
                    ["Unit"] = "Viên",
                    ["Quantity"] = 20_000,
                    ["UnitPrice"] = 450m,
                    ["Amount"] = 9_000_000m,
                });
                break;
            case TemplateKind.WarehouseReceipt:
                data["Receipt"] = new JsonObject
                {
                    ["Number"] = "NK-2026-0001",
                    ["Date"] = "2026-10-24",
                    ["TypeText"] = "PHIẾU NHẬP KHO",
                    ["IsReturn"] = false,
                    ["IsVirtual"] = false,
                    ["WarehouseName"] = "Kho Thuốc chính",
                    ["DelivererName"] = "Nhân viên NCC1",
                    ["BidDecisionNumber"] = "1234/QĐ-BV",
                    ["ContractNumber"] = "15/2026/HĐ-BV",
                    ["InvoiceSymbol"] = "1C26TAA",
                    ["InvoiceNumber"] = "0000123",
                    ["InvoiceDate"] = "2026-10-24",
                    ["AmountBeforeVat"] = 8_571_429m,
                    ["VatAmount"] = 428_571m,
                    ["TotalAmount"] = 9_000_000m,
                };
                data["Lines"] = new JsonArray(new JsonObject
                {
                    ["ItemCode"] = "TH001",
                    ["ItemName"] = "Paracetamol 500mg",
                    ["Unit"] = "Viên",
                    ["LotNumber"] = "L2610",
                    ["ExpiryDate"] = "2028-09-30",
                    ["Quantity"] = 20_000,
                    ["UnitPrice"] = 450m,
                    ["Amount"] = 9_000_000m,
                });
                break;
            case TemplateKind.Acceptance:
                data["Acceptance"] = new JsonObject
                {
                    ["Number"] = "NT-2026-0010",
                    ["Date"] = "2026-11-01",
                    ["PeriodText"] = "Tháng 10/2026",
                    ["TotalAmount"] = 9_000_000m,
                    ["Conclusion"] = "Hàng hóa đúng chủng loại, số lượng, chất lượng theo hợp đồng. Đồng ý nghiệm thu.",
                };
                data["Receipts"] = new JsonArray(new JsonObject { ["Number"] = "NK-2026-0001", ["Date"] = "2026-10-24", ["InvoiceNumber"] = "0000123", ["Amount"] = 9_000_000m });
                data["Items"] = new JsonArray(new JsonObject { ["Name"] = "Paracetamol 500mg", ["Unit"] = "Viên", ["Quantity"] = 20_000, ["UnitPrice"] = 450m, ["Amount"] = 9_000_000m });
                break;
            case TemplateKind.Valuation:
                data["Valuation"] = new JsonObject
                {
                    ["Number"] = "08a-2026-0003",
                    ["Date"] = "2026-11-05",
                    ["PeriodText"] = "Tháng 10/2026",
                    ["PaymentNumber"] = 3,
                    ["ContractValue"] = 1_205_030_000m,
                    ["AdvanceAmount"] = 0m,
                    ["PreviousCumulative"] = 300_000_000m,
                    ["CurrentPeriodValue"] = 9_000_000m,
                    ["CumulativeToDate"] = 309_000_000m,
                    ["CompletionPercent"] = 25.64m,
                    ["AdvanceRecovered"] = 0m,
                    ["RequestedAmount"] = 9_000_000m,
                };
                data["Items"] = new JsonArray(new JsonObject
                {
                    ["Name"] = "Paracetamol 500mg",
                    ["Unit"] = "Viên",
                    ["ContractQuantity"] = 200_000,
                    ["UnitPrice"] = 450m,
                    ["PreviousQuantity"] = 60_000,
                    ["CurrentQuantity"] = 20_000,
                    ["CumulativeQuantity"] = 80_000,
                    ["ContractAmount"] = 90_000_000m,
                    ["PreviousAmount"] = 27_000_000m,
                    ["CurrentAmount"] = 9_000_000m,
                    ["CumulativeAmount"] = 36_000_000m,
                });
                break;
            case TemplateKind.PaymentRequest:
                data["Payment"] = new JsonObject
                {
                    ["Number"] = "ĐNTT-2026-0003",
                    ["Date"] = "2026-11-05",
                    ["ValuationNumber"] = "08a-2026-0003",
                    ["RequestedAmount"] = 9_000_000m,
                    ["PaidBefore"] = 300_000_000m,
                    ["AdvanceRecovered"] = 0m,
                    ["PaymentOrderNumber"] = "",
                };
                break;
            case TemplateKind.Liquidation:
                data["Liquidation"] = new JsonObject
                {
                    ["Number"] = "TL-2027-0001",
                    ["Date"] = "2027-03-15",
                    ["ContractValue"] = 1_205_030_000m,
                    ["ExecutedValue"] = 1_180_000_000m,
                    ["PaidAmount"] = 1_180_000_000m,
                    ["RemainingAmount"] = 0m,
                    ["ReductionAmount"] = 25_030_000m,
                    ["Notes"] = "Hai bên thống nhất không còn vướng mắc.",
                };
                data["Valuations"] = new JsonArray(new JsonObject
                {
                    ["Number"] = "08a-2026-0003",
                    ["Date"] = "2026-11-05",
                    ["Amount"] = 9_000_000m,
                    ["PaidAmount"] = 9_000_000m,
                    ["PaymentOrderNumber"] = "UNC 152",
                });
                break;
        }

        return data;
    }
}
