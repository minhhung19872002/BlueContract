using BlueContract.Domain.Documents;

namespace BlueContract.Infrastructure.Documents.Templates;

internal sealed record BuiltInTemplate(string Code, string Name, TemplateKind Kind, string Description, string FileName, Func<byte[]> Build);

/// <summary>
/// The 14 document templates shipped with the system (spec §10, items 3–16). They are generated from code so the
/// legal text, placeholders and signature anchors stay consistent with <see cref="Application.Documents.TemplateFieldCatalog"/>;
/// `--export-templates &lt;dir&gt;` writes them to templates/ for hospitals to customise and re-upload.
/// </summary>
internal static class BuiltInTemplates
{
    public static IReadOnlyList<BuiltInTemplate> All { get; } =
    [
        new("HD_THUOC", "Hợp đồng cung cấp thuốc", TemplateKind.Contract,
            "Hợp đồng theo đơn giá cố định, gọi hàng từng đợt — thuốc (trong thầu)", "03_HD_cung_cap_thuoc.docx",
            () => Contract(ContractVariant.Drug)),
        new("HD_VTYT", "Hợp đồng cung cấp vật tư y tế, hóa chất, sinh phẩm xét nghiệm", TemplateKind.Contract,
            "Hợp đồng theo đơn giá cố định, gọi hàng từng đợt — VTYT, hóa chất, sinh phẩm (trong thầu)", "04_HD_VTYT_hoa_chat_sinh_pham.docx",
            () => Contract(ContractVariant.MedicalSupply)),
        new("HD_HANGHOA_THAU", "Hợp đồng mua sắm hàng hóa, tài sản (có thầu)", TemplateKind.Contract,
            "Hợp đồng trọn gói — hàng hóa, tài sản, trang thiết bị", "05_HD_hang_hoa_tai_san_co_thau.docx",
            () => Contract(ContractVariant.AssetBidding)),
        new("HD_HANGHOA_KHONGTHAU", "Hợp đồng mua sắm hàng hóa, tài sản (không qua thầu)", TemplateKind.Contract,
            "Hợp đồng trọn gói — mua sắm trực tiếp/chỉ định, không có quyết định thầu", "06_HD_hang_hoa_tai_san_khong_thau.docx",
            () => Contract(ContractVariant.AssetDirect)),
        new("HD_DICHVU_THAU", "Hợp đồng sửa chữa, dịch vụ (có thầu)", TemplateKind.Contract,
            "Hợp đồng trọn gói — sửa chữa, dịch vụ", "07_HD_sua_chua_dich_vu_co_thau.docx",
            () => Contract(ContractVariant.ServiceBidding)),
        new("HD_DICHVU_KHONGTHAU", "Hợp đồng sửa chữa, dịch vụ (không qua thầu)", TemplateKind.Contract,
            "Hợp đồng trọn gói — sửa chữa, dịch vụ, không có quyết định thầu", "08_HD_sua_chua_dich_vu_khong_thau.docx",
            () => Contract(ContractVariant.ServiceDirect)),
        new("HD_DKC_DKCT", "Hợp đồng trong thầu (Điều kiện chung và Điều kiện cụ thể)", TemplateKind.Contract,
            "Mẫu hợp đồng theo hồ sơ mời thầu: Phần Điều kiện chung + Điều kiện cụ thể + Bảng giá", "09_HD_DKC_DKCT.docx",
            GeneralSpecificConditionsContract),
        new("PHIEU_NHAP_KHO", "Phiếu nhập kho / Phiếu hoàn trả", TemplateKind.WarehouseReceipt,
            "Phiếu nhập kho (hoặc hoàn trả) với 5 chữ ký", "10_Phieu_nhap_kho_hoan_tra.docx", WarehouseReceipt),
        new("DE_NGHI_THANH_TOAN", "Đề nghị thanh toán", TemplateKind.PaymentRequest,
            "Giấy đề nghị thanh toán kèm biên bản xác định giá trị", "11_De_nghi_thanh_toan.docx", PaymentRequest),
        new("BB_NGHIEM_THU", "Biên bản nghiệm thu", TemplateKind.Acceptance,
            "Biên bản nghiệm thu hàng hóa theo kỳ", "12_Bien_ban_nghiem_thu.docx", Acceptance),
        new("BB_08A", "Biên bản xác định giá trị khối lượng hoàn thành (Mẫu 08a)", TemplateKind.Valuation,
            "Mẫu số 08a/TT — xác định giá trị khối lượng công việc hoàn thành theo hợp đồng", "13_Mau_08a_xac_dinh_gia_tri.docx", Valuation),
        new("BB_THANH_LY", "Biên bản thanh lý hợp đồng", TemplateKind.Liquidation,
            "Thanh lý hợp đồng, đối chiếu giá trị và chốt công nợ", "14_Bien_ban_thanh_ly.docx", Liquidation),
        new("PHU_LUC", "Phụ lục hợp đồng (bổ sung / giảm giá / thay đổi thông tin / gia hạn)", TemplateKind.Appendix,
            "Phụ lục điều chỉnh hợp đồng", "15_Phu_luc_hop_dong.docx", Appendix),
        new("PHIEU_DE_XUAT", "Phiếu đề xuất gọi hàng", TemplateKind.Proposal,
            "Phiếu đề xuất của khoa/phòng có X–N–T từ HIS", "16a_Phieu_de_xuat.docx", Proposal),
        new("PHIEU_TONG_HOP", "Phiếu tổng hợp đề xuất", TemplateKind.ProposalSummary,
            "Tổng hợp đề xuất theo mảng, cột số lượng duyệt", "16b_Phieu_tong_hop_de_xuat.docx", ProposalSummary),
        new("DON_GOI_HANG", "Đơn gọi hàng", TemplateKind.CallOrder,
            "Đơn gọi hàng gửi nhà cung cấp", "16c_Don_goi_hang.docx", CallOrder),
    ];

    private enum ContractVariant
    {
        Drug,
        MedicalSupply,
        AssetBidding,
        AssetDirect,
        ServiceBidding,
        ServiceDirect,
    }

    private static readonly string[] CommonLegalBases =
    [
        "- Căn cứ Bộ luật Dân sự số 91/2015/QH13 ngày 24 tháng 11 năm 2015;",
        "- Căn cứ Luật Thương mại số 36/2005/QH11 ngày 14 tháng 6 năm 2005;",
        "- Căn cứ Luật Giao dịch điện tử số 20/2023/QH15 ngày 22 tháng 6 năm 2023;",
    ];

    private static readonly string[] BiddingLegalBases =
    [
        "- Căn cứ Luật Đấu thầu số 22/2023/QH15 ngày 23 tháng 6 năm 2023;",
        "- Căn cứ Nghị định số 24/2024/NĐ-CP ngày 27 tháng 02 năm 2024 của Chính phủ quy định chi tiết một số điều và biện pháp thi hành Luật Đấu thầu về lựa chọn nhà thầu;",
    ];

    // ---------------- Contracts ----------------

    private static byte[] Contract(ContractVariant v)
    {
        var bidding = v is ContractVariant.Drug or ContractVariant.MedicalSupply or ContractVariant.AssetBidding or ContractVariant.ServiceBidding;
        var callOff = v is ContractVariant.Drug or ContractVariant.MedicalSupply;
        var (title, subject) = v switch
        {
            ContractVariant.Drug => ("HỢP ĐỒNG CUNG CẤP THUỐC", "cung cấp thuốc"),
            ContractVariant.MedicalSupply => ("HỢP ĐỒNG CUNG CẤP VẬT TƯ Y TẾ, HÓA CHẤT, SINH PHẨM XÉT NGHIỆM", "cung cấp vật tư y tế, hóa chất, sinh phẩm xét nghiệm"),
            ContractVariant.AssetBidding or ContractVariant.AssetDirect => ("HỢP ĐỒNG MUA SẮM HÀNG HÓA, TÀI SẢN", "mua sắm hàng hóa, tài sản"),
            _ => ("HỢP ĐỒNG CUNG CẤP DỊCH VỤ, SỬA CHỮA", "cung cấp dịch vụ, sửa chữa"),
        };

        var b = new DocxBuilder().Motto()
            .Title(title, "Số: {{Contract.Number}}", "Về việc: {{Contract.Name}}");

        b.List([.. CommonLegalBases]);
        if (bidding)
        {
            b.List([.. BiddingLegalBases]);
        }

        if (v == ContractVariant.Drug)
        {
            b.List("- Căn cứ Luật Dược số 105/2016/QH13 ngày 06 tháng 4 năm 2016;",
                "- Căn cứ Thông tư số 07/2024/TT-BYT ngày 17 tháng 5 năm 2024 của Bộ trưởng Bộ Y tế quy định về đấu thầu thuốc tại cơ sở y tế công lập;");
        }
        else if (v == ContractVariant.MedicalSupply)
        {
            b.List("- Căn cứ Nghị định số 98/2021/NĐ-CP ngày 08 tháng 11 năm 2021 của Chính phủ về quản lý trang thiết bị y tế và các văn bản sửa đổi, bổ sung;");
        }

        if (bidding)
        {
            b.List("- Căn cứ Quyết định số {{BidDecision.Number}} ngày {{BidDecision.Date | date}} của {{Organization.Name}} về việc phê duyệt kết quả lựa chọn nhà thầu {{BidDecision.PackageName}};");
        }

        b.List("- Căn cứ nhu cầu và khả năng của hai bên.")
            .P("Hôm nay, {{Contract.SignedDate | datetext}}, tại {{Organization.Name}}, chúng tôi gồm:", indent: true);

        Parties(b);
        b.P("Hai bên thống nhất ký kết hợp đồng " + subject + " với các điều khoản sau:");

        b.Heading("Điều 1. Đối tượng và giá trị hợp đồng")
            .P("Bên B cung cấp cho Bên A các hàng hóa, dịch vụ theo danh mục dưới đây:");
        b.Table("Items", ItemColumns(v), ["{{Contract.TotalAmount | money}}"], "Tổng giá trị hợp đồng (đã bao gồm thuế GTGT)");
        b.P("Bằng chữ: {{Contract.TotalAmount | words}}.", italic: true)
            .P("Giá trị trước thuế: {{Contract.AmountBeforeVat | money}} đồng; thuế GTGT: {{Contract.VatAmount | money}} đồng.");

        b.Heading("Điều 2. Loại hợp đồng và giá hợp đồng");
        if (callOff)
        {
            b.P("Hợp đồng theo đơn giá cố định. Đơn giá không thay đổi trong suốt thời gian thực hiện hợp đồng; giá trị thanh toán được tính theo số lượng thực tế Bên A gọi hàng, đã nghiệm thu và không vượt số lượng của hợp đồng.")
                .P("Số lượng trong hợp đồng là số lượng tối đa; Bên A không bắt buộc phải mua hết số lượng. Phần không thực hiện được xác định khi thanh lý hợp đồng.");
        }
        else
        {
            b.P("Hợp đồng trọn gói. Giá hợp đồng không thay đổi trong suốt thời gian thực hiện đối với khối lượng công việc thuộc phạm vi hợp đồng.");
        }

        b.Heading("Điều 3. Chất lượng, tiêu chuẩn kỹ thuật");
        switch (v)
        {
            case ContractVariant.Drug:
                b.P("Thuốc cung cấp phải đúng tên, hoạt chất, nồng độ/hàm lượng, dạng bào chế, quy cách đóng gói, hãng và nước sản xuất, số đăng ký lưu hành hoặc giấy phép nhập khẩu còn hiệu lực như đã trúng thầu; đạt tiêu chuẩn chất lượng theo hồ sơ đăng ký.")
                    .P("Bảo quản, vận chuyển theo đúng điều kiện ghi trên nhãn và quy định về Thực hành tốt bảo quản thuốc (GSP). Mỗi lô hàng phải có phiếu kiểm nghiệm của nhà sản xuất.")
                    .P("{{Contract.WarrantyTerms}}");
                break;
            case ContractVariant.MedicalSupply:
                b.P("Hàng hóa phải mới 100%, đúng chủng loại, thông số kỹ thuật, hãng, nước sản xuất, số lưu hành/giấy phép nhập khẩu như đã trúng thầu; có đầy đủ CO, CQ (nếu có) và hướng dẫn sử dụng bằng tiếng Việt.")
                    .P("{{Contract.WarrantyTerms}}");
                break;
            case ContractVariant.AssetBidding or ContractVariant.AssetDirect:
                b.P("Hàng hóa mới 100%, sản xuất năm 2025 trở về sau, đúng cấu hình, thông số kỹ thuật theo danh mục; có CO, CQ, catalogue và tài liệu hướng dẫn sử dụng bằng tiếng Việt. Bên B lắp đặt, chạy thử, hướng dẫn sử dụng tại địa điểm của Bên A.")
                    .P("Bảo hành: {{Contract.WarrantyTerms}}");
                break;
            default:
                b.P("Bên B thực hiện dịch vụ/sửa chữa đúng phạm vi, khối lượng và yêu cầu kỹ thuật; vật tư thay thế phải mới 100%, đúng chủng loại. Mọi phát sinh ngoài phạm vi phải được Bên A chấp thuận bằng văn bản trước khi thực hiện.")
                    .P("Bảo hành: {{Contract.WarrantyTerms}}");
                break;
        }

        b.Heading("Điều 4. Thời gian, địa điểm và phương thức giao hàng")
            .P("Thời gian thực hiện hợp đồng: {{Contract.Duration}}, từ ngày {{Contract.EffectiveDate | date}} đến ngày {{Contract.ExpiryDate | date}}.")
            .P("{{Contract.DeliveryTerms}}");
        if (callOff)
        {
            b.P("Bên A gọi hàng theo từng đợt trên Cổng Quản lý & Thanh toán Mua sắm Hàng hóa Trực tuyến của Bên A. Bên B xác nhận đơn gọi hàng trên cổng trong vòng 24 giờ kể từ khi nhận được thông báo; trường hợp không đáp ứng đủ số lượng phải nêu rõ lý do.");
        }

        b.P("Khi giao hàng, Bên B xuất hóa đơn giá trị gia tăng (hóa đơn điện tử) đúng hàng hóa, số lượng, đơn giá thực giao. Người giao hàng của Bên B ký xác nhận trên phiếu nhập kho điện tử.");

        b.Heading("Điều 5. Nghiệm thu và thanh toán")
            .P("Hằng tháng, căn cứ các phiếu nhập kho đã ký đầy đủ, hai bên lập biên bản nghiệm thu; sau đó lập biên bản xác định giá trị khối lượng hoàn thành (Mẫu 08a) và đề nghị thanh toán.")
            .P("Phương thức thanh toán: chuyển khoản vào tài khoản của Bên B nêu tại hợp đồng này. {{Contract.PaymentTerms}}")
            .P("Hồ sơ thanh toán gồm: biên bản nghiệm thu, biên bản xác định giá trị khối lượng hoàn thành, hóa đơn GTGT, giấy đề nghị thanh toán và các tài liệu khác theo quy định của Kho bạc Nhà nước.");

        b.If("Contract.GuaranteeText", x => x
            .Heading("Điều 6. Bảo đảm thực hiện hợp đồng")
            .P("{{Contract.GuaranteeText}}")
            .P("Bên B không được hoàn trả bảo đảm thực hiện hợp đồng trong trường hợp từ chối thực hiện hợp đồng sau khi hợp đồng có hiệu lực hoặc vi phạm thỏa thuận trong hợp đồng."));

        b.Heading("Điều 7. Quyền và nghĩa vụ của Bên A")
            .List(
                "- Gọi hàng, tiếp nhận, kiểm tra và nghiệm thu hàng hóa theo hợp đồng;",
                "- Thanh toán cho Bên B đầy đủ, đúng hạn theo Điều 5;",
                "- Từ chối nhận hàng không đúng chủng loại, chất lượng, hạn dùng hoặc giao không đúng thời hạn;",
                "- Ký số các tài liệu thuộc hồ sơ hợp đồng trên Cổng của Bên A theo đúng quy trình.");

        b.Heading("Điều 8. Quyền và nghĩa vụ của Bên B")
            .List(
                "- Cung cấp hàng hóa, dịch vụ đúng số lượng, chất lượng, tiến độ theo hợp đồng;",
                "- Chịu trách nhiệm về nguồn gốc, xuất xứ, chất lượng hàng hóa; thu hồi, đổi trả hàng hóa không đạt yêu cầu bằng chi phí của mình;",
                "- Rà soát, ký số hợp đồng và các tài liệu thanh toán trên Cổng của Bên A; bảo đảm chứng thư số dùng để ký còn hiệu lực;",
                "- Được thanh toán đầy đủ, đúng hạn theo hợp đồng.");

        b.Heading("Điều 9. Phạt vi phạm và bồi thường thiệt hại")
            .P("Bên vi phạm nghĩa vụ hợp đồng phải chịu phạt vi phạm với mức 8% giá trị phần nghĩa vụ hợp đồng bị vi phạm và bồi thường thiệt hại thực tế (nếu có) theo quy định của Luật Thương mại.")
            .P("Trường hợp Bên B chậm giao hàng quá 15 ngày so với thời hạn mà không có lý do chính đáng, Bên A có quyền mua hàng của nhà cung cấp khác; Bên B chịu phần chênh lệch giá (nếu có).");

        b.Heading("Điều 10. Bất khả kháng")
            .P("Sự kiện bất khả kháng là sự kiện xảy ra một cách khách quan, không thể lường trước và không thể khắc phục được mặc dù đã áp dụng mọi biện pháp cần thiết. Bên gặp sự kiện bất khả kháng phải thông báo bằng văn bản cho bên kia trong vòng 07 ngày.");

        b.Heading("Điều 11. Giải quyết tranh chấp")
            .P("Các tranh chấp phát sinh được giải quyết trên cơ sở thương lượng; trường hợp không thương lượng được thì một trong hai bên có quyền khởi kiện tại Tòa án nhân dân có thẩm quyền.");

        b.Heading("Điều 12. Hiệu lực hợp đồng")
            .P("Hợp đồng có hiệu lực kể từ ngày {{Contract.EffectiveDate | date}} và được thanh lý khi hai bên hoàn thành nghĩa vụ.")
            .P("Hợp đồng được lập dưới dạng thông điệp dữ liệu, ký số bởi đại diện hợp pháp của hai bên và đóng dấu điện tử của Bên A; có giá trị pháp lý như văn bản giấy theo Luật Giao dịch điện tử.");

        ContractSignatures(b);
        return b.Build();
    }

    private static void Parties(DocxBuilder b) =>
        b.Heading("BÊN MUA (Bên A): {{Organization.Name | upper}}")
            .List(
                "- Địa chỉ: {{Organization.Address}}",
                "- Điện thoại: {{Organization.Phone}}",
                "- Mã số thuế/Mã ĐVQHNS: {{Organization.TaxCode}}",
                "- Tài khoản: {{Organization.TreasuryAccountNumber}} tại {{Organization.TreasuryName}}",
                "- Đại diện: Ông/Bà {{Organization.RepresentativeName}} — Chức vụ: {{Organization.RepresentativeTitle}}")
            .Heading("BÊN BÁN (Bên B): {{Supplier.Name | upper}}")
            .List(
                "- Địa chỉ: {{Supplier.Address}}",
                "- Điện thoại: {{Supplier.Phone}} — Email: {{Supplier.Email}}",
                "- Mã số thuế: {{Supplier.TaxCode}}",
                "- Tài khoản: {{Supplier.BankAccountNumber}} tại {{Supplier.BankName}}",
                "- Đại diện: Ông/Bà {{Supplier.RepresentativeName}} — Chức vụ: {{Supplier.RepresentativeTitle}}{{#if Supplier.AuthorizationDocument}} (theo Giấy ủy quyền {{Supplier.AuthorizationDocument}}){{/if}}");

    private static void ContractSignatures(DocxBuilder b) =>
        b.Signatures(
                new SignatureCol("ĐẠI DIỆN BÊN B", "(Ký số, ghi rõ họ tên)", "SIG_SUPPLIER", "{{Supplier.RepresentativeName}}"),
                new SignatureCol("ĐẠI DIỆN BÊN A", "(Ký số, đóng dấu điện tử)", "SIG_DIRECTOR", "{{Organization.RepresentativeName}}"))
            .P("{{SIG_CLERK_SEAL}}", align: Align.Right, indent: false)
            .P("Kế toán (ký nháy): {{SIG_ACCOUNTANT_INITIAL}}", italic: true, align: Align.Left, indent: false);

    private static List<Col> ItemColumns(ContractVariant v) => v switch
    {
        ContractVariant.Drug =>
        [
            new("STT", "{{@index}}", 5, Align.Center),
            new("Tên thuốc", "{{Name}}", 16),
            new("Hoạt chất", "{{ActiveIngredient}}", 13),
            new("Quy cách", "{{Specification}}", 12),
            new("ĐVT", "{{Unit}}", 6, Align.Center),
            new("Hãng, nước SX", "{{Manufacturer}}, {{Country}}", 12),
            new("SĐK/GPNK", "{{RegistrationNumber}}", 9),
            new("Số lượng", "{{Quantity | number}}", 8, Align.Right),
            new("Đơn giá", "{{UnitPrice | money}}", 8, Align.Right),
            new("Thành tiền", "{{Amount | money}}", 11, Align.Right),
        ],
        ContractVariant.MedicalSupply =>
        [
            new("STT", "{{@index}}", 5, Align.Center),
            new("Tên hàng hóa", "{{Name}}", 18),
            new("Thông số kỹ thuật", "{{ActiveIngredient}}", 17),
            new("Quy cách", "{{Specification}}", 10),
            new("ĐVT", "{{Unit}}", 6, Align.Center),
            new("Hãng, nước SX", "{{Manufacturer}}, {{Country}}", 13),
            new("Số lượng", "{{Quantity | number}}", 9, Align.Right),
            new("Đơn giá", "{{UnitPrice | money}}", 10, Align.Right),
            new("Thành tiền", "{{Amount | money}}", 12, Align.Right),
        ],
        _ =>
        [
            new("STT", "{{@index}}", 6, Align.Center),
            new("Tên hàng hóa, dịch vụ", "{{Name}}", 26),
            new("Mô tả, thông số", "{{ActiveIngredient}}", 22),
            new("ĐVT", "{{Unit}}", 8, Align.Center),
            new("Số lượng", "{{Quantity | number}}", 10, Align.Right),
            new("Đơn giá", "{{UnitPrice | money}}", 13, Align.Right),
            new("Thành tiền", "{{Amount | money}}", 15, Align.Right),
        ],
    };

    private static byte[] GeneralSpecificConditionsContract()
    {
        var b = new DocxBuilder().Motto()
            .Title("HỢP ĐỒNG", "Số: {{Contract.Number}}", "Gói thầu: {{BidDecision.PackageName}}", "Thuộc dự toán mua sắm: {{Contract.Name}}");
        b.List([.. CommonLegalBases, .. BiddingLegalBases])
            .List("- Căn cứ Quyết định số {{BidDecision.Number}} ngày {{BidDecision.Date | date}} về việc phê duyệt kết quả lựa chọn nhà thầu;",
                "- Căn cứ hồ sơ mời thầu, hồ sơ dự thầu và biên bản hoàn thiện hợp đồng.")
            .P("Hôm nay, {{Contract.SignedDate | datetext}}, tại {{Organization.Name}}, chúng tôi gồm:");
        Parties(b);
        b.P("Hai bên thỏa thuận ký kết hợp đồng gồm: Phần I — Điều kiện chung, Phần II — Điều kiện cụ thể, Phần III — Bảng giá hợp đồng, với nội dung như sau.");

        b.Title("PHẦN I. ĐIỀU KIỆN CHUNG CỦA HỢP ĐỒNG");
        b.Heading("1. Định nghĩa")
            .P("Trong hợp đồng này, “Chủ đầu tư” là Bên A; “Nhà thầu” là Bên B; “Hàng hóa” là các mặt hàng nêu tại Phần III; “Cổng” là Cổng Quản lý & Thanh toán Mua sắm Hàng hóa Trực tuyến của Chủ đầu tư.")
            .Heading("2. Hồ sơ hợp đồng và thứ tự ưu tiên")
            .P("Hồ sơ hợp đồng gồm theo thứ tự ưu tiên: (a) văn bản hợp đồng; (b) biên bản hoàn thiện hợp đồng; (c) quyết định phê duyệt kết quả lựa chọn nhà thầu; (d) Điều kiện cụ thể; (đ) Điều kiện chung; (e) hồ sơ dự thầu; (g) hồ sơ mời thầu; (h) các tài liệu kèm theo khác.")
            .Heading("3. Luật và ngôn ngữ")
            .P("Hợp đồng chịu sự điều chỉnh của pháp luật Việt Nam; ngôn ngữ của hợp đồng là tiếng Việt.")
            .Heading("4. Bảo đảm thực hiện hợp đồng")
            .P("Nhà thầu nộp bảo đảm thực hiện hợp đồng theo quy định tại Điều kiện cụ thể trước thời điểm hợp đồng có hiệu lực.")
            .Heading("5. Phạm vi cung cấp, tiến độ và giao hàng")
            .P("Phạm vi cung cấp nêu tại Phần III. Chủ đầu tư gọi hàng theo từng đợt trên Cổng; Nhà thầu xác nhận và giao hàng theo tiến độ tại Điều kiện cụ thể.")
            .Heading("6. Nghiệm thu, thanh toán")
            .P("Nghiệm thu theo kỳ trên cơ sở phiếu nhập kho đã ký; thanh toán theo biên bản xác định giá trị khối lượng hoàn thành và điều khoản tại Điều kiện cụ thể.")
            .Heading("7. Điều chỉnh, bổ sung hợp đồng")
            .P("Việc điều chỉnh khối lượng, giá, thời gian thực hiện được lập thành phụ lục hợp đồng và ký số theo quy trình như hợp đồng.")
            .Heading("8. Phạt vi phạm, chấm dứt hợp đồng, bất khả kháng, giải quyết tranh chấp")
            .P("Thực hiện theo Điều kiện cụ thể và quy định của Bộ luật Dân sự, Luật Thương mại, Luật Đấu thầu.");

        b.Title("PHẦN II. ĐIỀU KIỆN CỤ THỂ CỦA HỢP ĐỒNG");
        b.KeyValueTable(32,
            ("ĐKC 4 — Bảo đảm thực hiện hợp đồng", "{{Contract.GuaranteeText}}"),
            ("ĐKC 5 — Thời gian thực hiện", "{{Contract.Duration}}, từ {{Contract.EffectiveDate | date}} đến {{Contract.ExpiryDate | date}}"),
            ("ĐKC 5 — Giao hàng", "{{Contract.DeliveryTerms}}"),
            ("ĐKC 6 — Thanh toán", "{{Contract.PaymentTerms}}"),
            ("ĐKC 6 — Bảo hành, hạn dùng", "{{Contract.WarrantyTerms}}"),
            ("Nguồn vốn", "{{Contract.FundingSource}}"),
            ("Loại hợp đồng", "Hợp đồng theo đơn giá cố định"),
            ("ĐKC 8 — Phạt vi phạm", "8% giá trị phần nghĩa vụ hợp đồng bị vi phạm"));

        b.Title("PHẦN III. BẢNG GIÁ HỢP ĐỒNG");
        b.Table("Items", ItemColumns(ContractVariant.Drug), ["{{Contract.TotalAmount | money}}"], "Tổng cộng (đã bao gồm thuế GTGT)");
        b.P("Bằng chữ: {{Contract.TotalAmount | words}}.", italic: true);
        ContractSignatures(b);
        return b.Build();
    }

    // ---------------- Other documents ----------------

    private static byte[] WarehouseReceipt()
    {
        var b = new DocxBuilder().Landscape()
            .NationalHeader("{{Organization.Name | upper}}", "{{Receipt.WarehouseName}}", "Số: {{Receipt.Number}}", "{{Document.Place}}, {{Receipt.Date | datetext}}")
            .Title("{{Receipt.TypeText}}", "{{#if Receipt.IsVirtual}}(Phiếu xác nhận khối lượng — không qua kho){{/if}}")
            .List(
                "- Họ tên người giao hàng: {{Receipt.DelivererName}} — Đơn vị: {{Supplier.Name}} (MST {{Supplier.TaxCode}})",
                "- Theo hợp đồng số {{Receipt.ContractNumber}}; Quyết định thầu số {{Receipt.BidDecisionNumber}}",
                "- Hóa đơn: ký hiệu {{Receipt.InvoiceSymbol}}, số {{Receipt.InvoiceNumber}}, ngày {{Receipt.InvoiceDate | date}}",
                "- {{#if Receipt.IsReturn}}Hoàn trả{{/if}}{{#if !Receipt.IsReturn}}Nhập tại{{/if}} kho: {{Receipt.WarehouseName}}");
        b.Table("Lines",
        [
            new("STT", "{{@index}}", 5, Align.Center),
            new("Mã hàng", "{{ItemCode}}", 9),
            new("Tên hàng hóa", "{{ItemName}}", 26),
            new("ĐVT", "{{Unit}}", 7, Align.Center),
            new("Số lô", "{{LotNumber}}", 9),
            new("Hạn dùng", "{{ExpiryDate | date}}", 10, Align.Center),
            new("Số lượng", "{{Quantity | number}}", 10, Align.Right),
            new("Đơn giá", "{{UnitPrice | money}}", 10, Align.Right),
            new("Thành tiền", "{{Amount | money}}", 14, Align.Right),
        ], ["{{Receipt.TotalAmount | money}}"], "Tổng cộng");
        b.P("Tổng số tiền (bằng chữ): {{Receipt.TotalAmount | words}}.", italic: true)
            .Signatures(
                new SignatureCol("Thủ kho", "(Ký, họ tên)", "SIG_STOREKEEPER", null),
                new SignatureCol("Người giao hàng", "(Ký, họ tên)", "SIG_DELIVERER", "{{Receipt.DelivererName}}"),
                new SignatureCol("Kế toán kho", "(Ký, họ tên)", "SIG_WAREHOUSE_ACCOUNTANT", null),
                new SignatureCol("Trưởng khoa", "(Ký, họ tên)", "SIG_DEPT_HEAD", null),
                new SignatureCol("Kế toán trưởng", "(Ký, họ tên)", "SIG_CHIEF_ACCOUNTANT", null));
        return b.Build();
    }

    private static byte[] PaymentRequest()
    {
        var b = new DocxBuilder()
            .NationalHeader("{{Supplier.Name | upper}}", null, "Số: {{Payment.Number}}", "{{Document.Place}}, {{Payment.Date | datetext}}")
            .Title("GIẤY ĐỀ NGHỊ THANH TOÁN")
            .P("Kính gửi: {{Organization.Name}}", bold: true, align: Align.Center, indent: false)
            .List(
                "- Căn cứ hợp đồng số {{Contract.Number}} ký ngày {{Contract.SignedDate | date}} về việc {{Contract.Name}};",
                "- Căn cứ biên bản xác định giá trị khối lượng hoàn thành số {{Payment.ValuationNumber}}.")
            .P("{{Supplier.Name}} đề nghị {{Organization.Name}} thanh toán như sau:")
            .KeyValueTable(55,
                ("1. Giá trị hợp đồng", "{{Contract.TotalAmount | money}} đồng"),
                ("2. Đã thanh toán các kỳ trước", "{{Payment.PaidBefore | money}} đồng"),
                ("3. Thu hồi tạm ứng", "{{Payment.AdvanceRecovered | money}} đồng"),
                ("4. Số tiền đề nghị thanh toán kỳ này", "{{Payment.RequestedAmount | money}} đồng"))
            .P("Bằng chữ: {{Payment.RequestedAmount | words}}.", italic: true)
            .P("Đề nghị chuyển khoản vào tài khoản số {{Supplier.BankAccountNumber}} tại {{Supplier.BankName}}, đơn vị thụ hưởng: {{Supplier.Name}}.")
            .Signatures(
                new SignatureCol("ĐẠI DIỆN BÊN A", "(Duyệt)", "SIG_DIRECTOR", "{{Organization.RepresentativeName}}"),
                new SignatureCol("ĐẠI DIỆN ĐƠN VỊ ĐỀ NGHỊ", "(Ký số, ghi rõ họ tên)", "SIG_SUPPLIER", "{{Supplier.RepresentativeName}}"));
        return b.Build();
    }

    private static byte[] Acceptance()
    {
        var b = new DocxBuilder().Motto()
            .Title("BIÊN BẢN NGHIỆM THU", "Số: {{Acceptance.Number}}", "Hợp đồng số {{Contract.Number}} — Kỳ: {{Acceptance.PeriodText}}")
            .List("- Căn cứ hợp đồng số {{Contract.Number}} ngày {{Contract.SignedDate | date}} giữa {{Organization.Name}} và {{Supplier.Name}};",
                "- Căn cứ các phiếu nhập kho đã được ký xác nhận đầy đủ trong kỳ.")
            .P("Hôm nay, {{Acceptance.Date | datetext}}, tại {{Organization.Name}}, chúng tôi gồm:");
        Parties(b);
        b.P("Hai bên cùng tiến hành nghiệm thu hàng hóa đã giao theo các phiếu nhập kho sau:")
            .Table("Receipts",
            [
                new("STT", "{{@index}}", 8, Align.Center),
                new("Số phiếu nhập kho", "{{Number}}", 28),
                new("Ngày", "{{Date | date}}", 18, Align.Center),
                new("Số hóa đơn", "{{InvoiceNumber}}", 20),
                new("Giá trị (đồng)", "{{Amount | money}}", 26, Align.Right),
            ], ["{{Acceptance.TotalAmount | money}}"], "Tổng cộng")
            .Heading("Tổng hợp hàng hóa nghiệm thu")
            .Table("Items",
            [
                new("STT", "{{@index}}", 7, Align.Center),
                new("Tên hàng hóa", "{{Name}}", 37),
                new("ĐVT", "{{Unit}}", 10, Align.Center),
                new("Số lượng", "{{Quantity | number}}", 13, Align.Right),
                new("Đơn giá", "{{UnitPrice | money}}", 15, Align.Right),
                new("Thành tiền", "{{Amount | money}}", 18, Align.Right),
            ])
            .P("Giá trị nghiệm thu: {{Acceptance.TotalAmount | money}} đồng (bằng chữ: {{Acceptance.TotalAmount | words}}).", bold: true)
            .Heading("Kết luận")
            .P("{{Acceptance.Conclusion}}")
            .Signatures(
                new SignatureCol("ĐẠI DIỆN BÊN B", "(Ký số, ghi rõ họ tên)", "SIG_SUPPLIER", "{{Supplier.RepresentativeName}}"),
                new SignatureCol("ĐẠI DIỆN BÊN A", "(Ký số, đóng dấu điện tử)", "SIG_DIRECTOR", "{{Organization.RepresentativeName}}"))
            .P("{{SIG_CLERK_SEAL}}", align: Align.Right, indent: false);
        return b.Build();
    }

    private static byte[] Valuation()
    {
        var b = new DocxBuilder().Landscape()
            .NationalHeader("Mẫu số 08a", null, "Số: {{Valuation.Number}}", null)
            .Title("BẢNG XÁC ĐỊNH GIÁ TRỊ KHỐI LƯỢNG CÔNG VIỆC HOÀN THÀNH", "Thanh toán lần thứ: {{Valuation.PaymentNumber}} — Kỳ: {{Valuation.PeriodText}}")
            .List(
                "1. Tên dự án/dự toán: {{Contract.Name}}",
                "2. Chủ đầu tư: {{Organization.Name}} — Mã ĐVQHNS: {{Organization.TaxCode}}",
                "3. Nhà thầu: {{Supplier.Name}} — MST: {{Supplier.TaxCode}}",
                "4. Hợp đồng số {{Contract.Number}} ngày {{Contract.SignedDate | date}}; giá trị hợp đồng: {{Valuation.ContractValue | money}} đồng",
                "5. Giá trị tạm ứng theo hợp đồng: {{Valuation.AdvanceAmount | money}} đồng",
                "6. Căn cứ xác định: các biên bản nghiệm thu đã ký trong kỳ");
        b.Table("Items",
        [
            new("STT", "{{@index}}", 4, Align.Center),
            new("Tên hàng hóa, công việc", "{{Name}}", 18),
            new("ĐVT", "{{Unit}}", 5, Align.Center),
            new("KL theo HĐ", "{{ContractQuantity | number}}", 8, Align.Right),
            new("Đơn giá", "{{UnitPrice | money}}", 8, Align.Right),
            new("KL lũy kế đến cuối kỳ trước", "{{PreviousQuantity | number}}", 9, Align.Right),
            new("KL thực hiện kỳ này", "{{CurrentQuantity | number}}", 8, Align.Right),
            new("KL lũy kế đến hết kỳ này", "{{CumulativeQuantity | number}}", 8, Align.Right),
            new("Thành tiền theo HĐ", "{{ContractAmount | money}}", 9, Align.Right),
            new("Lũy kế đến cuối kỳ trước", "{{PreviousAmount | money}}", 8, Align.Right),
            new("Thực hiện kỳ này", "{{CurrentAmount | money}}", 7, Align.Right),
            new("Lũy kế đến hết kỳ này", "{{CumulativeAmount | money}}", 8, Align.Right),
        ], ["{{Valuation.ContractValue | money}}", "{{Valuation.PreviousCumulative | money}}", "{{Valuation.CurrentPeriodValue | money}}", "{{Valuation.CumulativeToDate | money}}"], "Tổng cộng");
        b.KeyValueTable(60,
                ("1. Giá trị hợp đồng", "{{Valuation.ContractValue | money}} đồng"),
                ("2. Lũy kế giá trị thực hiện đến cuối kỳ trước", "{{Valuation.PreviousCumulative | money}} đồng"),
                ("3. Giá trị thực hiện kỳ này", "{{Valuation.CurrentPeriodValue | money}} đồng"),
                ("4. Lũy kế giá trị thực hiện đến hết kỳ này", "{{Valuation.CumulativeToDate | money}} đồng ({{Valuation.CompletionPercent | percent}} giá trị hợp đồng)"),
                ("5. Thu hồi tạm ứng", "{{Valuation.AdvanceRecovered | money}} đồng"),
                ("6. Giá trị đề nghị thanh toán kỳ này", "{{Valuation.RequestedAmount | money}} đồng"))
            .P("Số tiền đề nghị thanh toán (bằng chữ): {{Valuation.RequestedAmount | words}}.", italic: true)
            .Signatures(
                new SignatureCol("ĐẠI DIỆN NHÀ THẦU", "(Ký số, ghi rõ họ tên)", "SIG_SUPPLIER", "{{Supplier.RepresentativeName}}"),
                new SignatureCol("ĐẠI DIỆN CHỦ ĐẦU TƯ", "(Ký số, đóng dấu điện tử)", "SIG_DIRECTOR", "{{Organization.RepresentativeName}}"))
            .P("{{SIG_CLERK_SEAL}}", align: Align.Right, indent: false);
        return b.Build();
    }

    private static byte[] Liquidation()
    {
        var b = new DocxBuilder().Motto()
            .Title("BIÊN BẢN THANH LÝ HỢP ĐỒNG", "Số: {{Liquidation.Number}}", "Hợp đồng số {{Contract.Number}} ngày {{Contract.SignedDate | date}}")
            .List([.. CommonLegalBases])
            .List("- Căn cứ hợp đồng số {{Contract.Number}} và các phụ lục hợp đồng (nếu có);",
                "- Căn cứ các biên bản xác định giá trị khối lượng hoàn thành và chứng từ thanh toán.")
            .P("Hôm nay, {{Liquidation.Date | datetext}}, tại {{Organization.Name}}, chúng tôi gồm:");
        Parties(b);
        b.P("Hai bên thống nhất thanh lý hợp đồng với các nội dung sau:")
            .Heading("Điều 1. Giá trị hợp đồng và giá trị thực hiện")
            .KeyValueTable(60,
                ("1. Giá trị hợp đồng (kể cả phụ lục)", "{{Liquidation.ContractValue | money}} đồng"),
                ("2. Giá trị thực hiện", "{{Liquidation.ExecutedValue | money}} đồng"),
                ("3. Giá trị giảm trừ / không thực hiện", "{{Liquidation.ReductionAmount | money}} đồng"),
                ("4. Bên A đã thanh toán", "{{Liquidation.PaidAmount | money}} đồng"),
                ("5. Bên A còn phải thanh toán", "{{Liquidation.RemainingAmount | money}} đồng"))
            .P("Giá trị còn phải thanh toán (bằng chữ): {{Liquidation.RemainingAmount | words}}.", italic: true)
            .Heading("Điều 2. Các lần xác định giá trị và thanh toán")
            .Table("Valuations",
            [
                new("STT", "{{@index}}", 7, Align.Center),
                new("Biên bản XĐGT", "{{Number}}", 23),
                new("Ngày", "{{Date | date}}", 15, Align.Center),
                new("Giá trị", "{{Amount | money}}", 20, Align.Right),
                new("Đã thanh toán", "{{PaidAmount | money}}", 20, Align.Right),
                new("Số UNC", "{{PaymentOrderNumber}}", 15),
            ])
            .Heading("Điều 3. Kết luận")
            .P("Bên B đã hoàn thành nghĩa vụ cung cấp hàng hóa theo hợp đồng. Sau khi Bên A thanh toán số tiền còn lại (nếu có), hai bên không còn nghĩa vụ đối với nhau theo hợp đồng; hợp đồng chấm dứt kể từ ngày biên bản này được ký và đóng dấu điện tử.")
            .P("{{Liquidation.Notes}}")
            .Signatures(
                new SignatureCol("ĐẠI DIỆN BÊN B", "(Ký số, ghi rõ họ tên)", "SIG_SUPPLIER", "{{Supplier.RepresentativeName}}"),
                new SignatureCol("ĐẠI DIỆN BÊN A", "(Ký số, đóng dấu điện tử)", "SIG_DIRECTOR", "{{Organization.RepresentativeName}}"))
            .P("{{SIG_CLERK_SEAL}}", align: Align.Right, indent: false);
        return b.Build();
    }

    private static byte[] Appendix()
    {
        var b = new DocxBuilder().Motto()
            .Title("PHỤ LỤC HỢP ĐỒNG SỐ {{Appendix.Number}}", "Hợp đồng số {{Contract.Number}} ngày {{Contract.SignedDate | date}}", "Nội dung: {{Appendix.TypeText}}")
            .List([.. CommonLegalBases])
            .List("- Căn cứ hợp đồng số {{Contract.Number}} về việc {{Contract.Name}};",
                "- Căn cứ nhu cầu thực tế: {{Appendix.Reason}}.")
            .P("Hôm nay, {{Appendix.Date | datetext}}, tại {{Organization.Name}}, chúng tôi gồm:");
        Parties(b);
        b.P("Hai bên thống nhất ký phụ lục điều chỉnh hợp đồng như sau:")
            .Heading("Điều 1. Nội dung điều chỉnh")
            .Table("Changes",
            [
                new("STT", "{{@index}}", 7, Align.Center),
                new("Mặt hàng / nội dung", "{{ItemName}}", 33),
                new("Thông tin điều chỉnh", "{{Field}}", 20),
                new("Theo hợp đồng", "{{OldValue}}", 20, Align.Right),
                new("Sau điều chỉnh", "{{NewValue}}", 20, Align.Right),
            ])
            .If("Appendix.ValueChange", x => x.P("Giá trị hợp đồng tăng/giảm: {{Appendix.ValueChange | money}} đồng. Giá trị hợp đồng sau điều chỉnh: {{Appendix.NewTotalAmount | money}} đồng ({{Appendix.NewTotalAmount | words}})."))
            .If("Appendix.NewExpiryDate", x => x.P("Thời hạn hợp đồng sau điều chỉnh: đến hết ngày {{Appendix.NewExpiryDate | date}}."))
            .Heading("Điều 2. Hiệu lực")
            .P("Phụ lục là bộ phận không tách rời của hợp đồng, có hiệu lực kể từ ngày ký số đầy đủ và đóng dấu điện tử. Các nội dung khác của hợp đồng giữ nguyên.");
        ContractSignatures(b);
        return b.Build();
    }

    private static byte[] Proposal()
    {
        var b = new DocxBuilder().Landscape()
            .NationalHeader("{{Organization.Name | upper}}", "{{Proposal.DepartmentName | upper}}", "Số: {{Proposal.Number}}", "{{Document.Place}}, {{Proposal.Date | datetext}}")
            .Title("PHIẾU ĐỀ XUẤT GỌI HÀNG", "Mảng: {{Proposal.DomainText}} — Kỳ: {{Proposal.Period}}");
        b.Table("Lines",
        [
            new("STT", "{{@index}}", 4, Align.Center),
            new("Tên hàng", "{{ItemName}}", 20),
            new("ĐVT", "{{Unit}}", 6, Align.Center),
            new("Số HĐ", "{{ContractNumber}}", 11),
            new("Tồn đầu kỳ", "{{OpeningStock | number}}", 8, Align.Right),
            new("Nhập", "{{Received | number}}", 8, Align.Right),
            new("Xuất", "{{Issued | number}}", 8, Align.Right),
            new("Tồn hiện tại", "{{CurrentStock | number}}", 8, Align.Right),
            new("SL còn lại HĐ", "{{ContractRemaining | number}}", 9, Align.Right),
            new("SL đề xuất", "{{ProposedQuantity | number}}", 8, Align.Right),
            new("Ghi chú", "{{Note}}", 10),
        ]);
        b.P("{{Proposal.Note}}")
            .Signatures(
                new SignatureCol("NGƯỜI LẬP", "(Ký, ghi rõ họ tên)", "SIG_CREATOR", "{{Proposal.CreatedByName}}"),
                new SignatureCol("TRƯỞNG KHOA/PHÒNG", "(Ký, ghi rõ họ tên)", "SIG_DEPT_HEAD", null));
        return b.Build();
    }

    private static byte[] ProposalSummary()
    {
        var b = new DocxBuilder().Landscape()
            .NationalHeader("{{Organization.Name | upper}}", null, "Số: {{Summary.Number}}", "{{Document.Place}}, {{Summary.Date | datetext}}")
            .Title("PHIẾU TỔNG HỢP ĐỀ XUẤT GỌI HÀNG", "Mảng: {{Summary.DomainText}} — Kỳ: {{Summary.Period}} — Gộp {{Summary.ProposalCount}} phiếu đề xuất");
        b.Table("Lines",
        [
            new("STT", "{{@index}}", 4, Align.Center),
            new("Tên hàng", "{{ItemName}}", 16),
            new("ĐVT", "{{Unit}}", 5, Align.Center),
            new("Nhà cung cấp", "{{SupplierName}}", 14),
            new("Số HĐ", "{{ContractNumber}}", 9),
            new("SL đề xuất", "{{ProposedQuantity | number}}", 7, Align.Right),
            new("Tồn đầu", "{{OpeningStock | number}}", 6, Align.Right),
            new("Nhập", "{{Received | number}}", 6, Align.Right),
            new("Xuất", "{{Issued | number}}", 6, Align.Right),
            new("Tồn", "{{CurrentStock | number}}", 6, Align.Right),
            new("SL duyệt", "{{ApprovedQuantity | number}}", 7, Align.Right),
            new("Đơn giá", "{{UnitPrice | money}}", 6, Align.Right),
            new("Thành tiền", "{{Amount | money}}", 8, Align.Right),
        ]);
        b.Signatures(
            new SignatureCol("NGƯỜI TỔNG HỢP", "(Ký, ghi rõ họ tên)", "SIG_AGGREGATOR", "{{Summary.AggregatorName}}"),
            new SignatureCol("TỔ TRƯỞNG", "(Ký, ghi rõ họ tên)", "SIG_DOMAIN_HEAD", null),
            new SignatureCol("GIÁM ĐỐC", "(Ký số)", "SIG_DIRECTOR", "{{Organization.RepresentativeName}}"));
        return b.Build();
    }

    private static byte[] CallOrder()
    {
        var b = new DocxBuilder()
            .NationalHeader("{{Organization.Name | upper}}", null, "Số: {{CallOrder.Number}}", "{{Document.Place}}, {{CallOrder.Date | datetext}}")
            .Title("ĐƠN GỌI HÀNG")
            .P("Kính gửi: {{Supplier.Name}}", bold: true, align: Align.Center, indent: false)
            .P("Căn cứ hợp đồng số {{Contract.Number}} và phiếu tổng hợp đề xuất số {{CallOrder.SummaryNumber}} đã được phê duyệt, {{Organization.Name}} đề nghị Quý công ty giao các mặt hàng sau:");
        b.Table("Lines",
        [
            new("STT", "{{@index}}", 7, Align.Center),
            new("Tên hàng", "{{ItemName}}", 38),
            new("ĐVT", "{{Unit}}", 10, Align.Center),
            new("Số lượng", "{{Quantity | number}}", 13, Align.Right),
            new("Đơn giá", "{{UnitPrice | money}}", 14, Align.Right),
            new("Thành tiền", "{{Amount | money}}", 18, Align.Right),
        ], ["{{CallOrder.TotalAmount | money}}"], "Tổng cộng");
        b.List(
                "- Địa điểm giao hàng: {{CallOrder.WarehouseName}} — {{CallOrder.WarehouseAddress}}",
                "- Thời hạn giao hàng: trước ngày {{CallOrder.DeliveryDeadline | date}}",
                "- Người liên hệ: {{CallOrder.ContactName}}")
            .P("Đề nghị Quý công ty xác nhận đơn gọi hàng trên Cổng mua sắm trực tuyến của bệnh viện trong vòng 24 giờ.")
            .Signatures(new SignatureCol("NGƯỜI GỌI HÀNG", "(Ký, ghi rõ họ tên)", "SIG_AGGREGATOR", null));
        return b.Build();
    }
}
