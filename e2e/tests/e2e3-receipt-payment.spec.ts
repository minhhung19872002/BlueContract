import { createHash, createHmac } from "node:crypto";
import { expect, request, test, type Page } from "@playwright/test";
import {
  apiAs,
  chooseOption,
  expectToast,
  loginAs,
  openFromInbox,
  signFromInbox,
} from "./helpers";

const HIS_KEY = process.env.HIS_INBOUND_API_KEY ?? "his-dev-key";
const HIS_SECRET =
  process.env.HIS_INBOUND_SECRET ?? "his-dev-secret-change-me-0123456789";

/**
 * E2E #3 (spec §13): HIS đẩy phiếu → 5 chữ ký → nghiệm thu → 08a (+ đề nghị thanh toán, UNC) → thanh lý.
 * Chuẩn bị: một hợp đồng sửa chữa ngoài thầu của NCC3 có hiệu lực (tạo qua API — luồng hợp đồng đã có E2E #1).
 * HIS là hệ thống bên ngoài nên phiếu được gửi thẳng vào API inbound có chữ ký HMAC; mọi bước còn lại qua giao diện.
 */
test("HIS receipt, five signatures, acceptance, valuation with payment order and liquidation", async ({
  browser,
  baseURL,
}) => {
  const today = new Date(Date.now() + 7 * 3600_000).toISOString().slice(0, 10);
  const code = Math.random().toString(36).slice(2, 8).toUpperCase();
  const contractNumber = `E2E3-${code}/HĐ`;
  const { contractId, item } = await prepareContract(
    baseURL!,
    contractNumber,
    code,
    today,
  );

  // 1. HIS đẩy phiếu nhập kho (API key + HMAC-SHA256).
  const receiptNumber = `PN-E2E-${code}`;
  const pushed = await pushReceipt(baseURL!, {
    hisReceiptId: `E2E-${code}`,
    receiptNumber,
    type: "Receipt",
    receiptDate: today,
    warehouseCode: "KTS01",
    bidDecisionNumber: null,
    supplierTaxCode: "2901234567",
    supplierName: null,
    contractNumber,
    callOrderNumber: null,
    delivererName: "Kỹ thuật viên Hồng Lam",
    note: null,
    invoice: {
      symbol: "1C26THL",
      number: code,
      date: today,
      amountBeforeVat: null,
      vatAmount: null,
      totalAmount: item.unitPrice,
      sellerTaxCode: "2901234567",
    },
    lines: [
      {
        hisKey: null,
        itemCode: item.itemCode,
        itemName: item.itemName,
        unit: item.unit,
        lotNumber: null,
        expiryDate: null,
        quantity: 1,
        unitPrice: item.unitPrice,
        vatRate: item.vatRate,
      },
    ],
  });
  expect(pushed.validationStatus).toBe("Valid");

  // 2. Năm chữ ký: Thủ kho → NCC → Kế toán kho → Trưởng khoa → Kế toán trưởng.
  const receiptTitle = `Phiếu nhập kho ${receiptNumber}`;
  const storekeeper = await loginAs(browser, "thukho");
  await signFromInbox(storekeeper, "", receiptTitle, /Ký số/);
  const supplier = await loginAs(browser, "ncc3");
  await signFromInbox(supplier, "/ncc", receiptTitle, /NCC ký số/);
  for (const user of ["ketoankho", "truongkhoa", "ketoantruong"]) {
    await signFromInbox(
      await loginAs(browser, user),
      "",
      receiptTitle,
      /Ký số/,
    );
  }
  const buyer = await loginAs(browser, "muasam");
  await buyer.goto(`/nhap-kho/${pushed.receiptId}`);
  await expect(buyer.getByText("Đã ký đủ").first()).toBeVisible();

  // 3. Biên bản nghiệm thu: chọn phiếu đã ký → gửi ký → Rà soát → NCC → GĐ → Văn thư.
  await buyer.goto("/thanh-toan/nghiem-thu/tao-moi");
  await chooseOption(buyer, buyer, "Hợp đồng", contractNumber, code);
  await buyer
    .getByRole("row", { name: new RegExp(receiptNumber) })
    .locator("input[type=checkbox]")
    .check();
  await buyer.getByRole("button", { name: /Lưu biên bản \(1 phiếu\)/ }).click();
  const acceptanceHeading = buyer.getByRole("heading", {
    name: /^Biên bản nghiệm thu NT-/,
  });
  await expect(acceptanceHeading).toBeVisible({ timeout: 60_000 });
  const acceptanceTitle = (await acceptanceHeading.textContent())!.trim();
  await buyer.getByRole("button", { name: "Gửi ký" }).click();
  await expectToast(buyer, "Đã gửi ký");
  const reviewer = await loginAs(browser, "rasoat");
  const director = await loginAs(browser, "giamdoc");
  const clerk = await loginAs(browser, "vanthu");
  await reviewAndSign(reviewer, supplier, director, clerk, acceptanceTitle);

  // 4. Biên bản 08a + đề nghị thanh toán.
  await buyer.goto("/thanh-toan/xac-dinh-gia-tri/tao-moi");
  await chooseOption(buyer, buyer, "Hợp đồng", contractNumber, code);
  await buyer
    .getByRole("row", {
      name: new RegExp(acceptanceTitle.replace("Biên bản nghiệm thu ", "")),
    })
    .locator("input[type=checkbox]")
    .check();
  await buyer.getByRole("button", { name: /Lập 08a \(1 biên bản\)/ }).click();
  const valuationHeading = buyer.getByRole("heading", {
    name: /^Biên bản xác định giá trị XD-/,
  });
  await expect(valuationHeading).toBeVisible({ timeout: 60_000 });
  const valuationNumber = (await valuationHeading.textContent())!
    .replace("Biên bản xác định giá trị ", "")
    .trim();
  const valuationUrl = buyer.url();
  await expect(buyer.getByText("100%").first()).toBeVisible();
  await buyer.getByRole("button", { name: "Gửi ký" }).click();
  await expectToast(buyer, "Đã gửi ký");
  await reviewAndSign(
    reviewer,
    supplier,
    director,
    clerk,
    `Biên bản xác định giá trị khối lượng hoàn thành (08a) ${valuationNumber}`,
  );
  const api = await apiAs(baseURL!, "muasam");
  const valuation = (await (
    await api.ctx.get(`/api/valuations/${valuationUrl.split("/").pop()}`, {
      headers: api.headers,
    })
  ).json()) as {
    paymentRequestNumber: string;
  };
  const requestTitle = `Đề nghị thanh toán ${valuation.paymentRequestNumber}`;
  await signFromInbox(supplier, "/ncc", requestTitle, /NCC ký số/);
  await signFromInbox(director, "", requestTitle, /Ký số/);

  // 5. Ủy nhiệm chi đủ số tiền đề nghị → Đã thanh toán.
  await buyer.goto(valuationUrl);
  await buyer.getByRole("button", { name: "Thêm UNC" }).click();
  const order = buyer.getByRole("dialog", { name: "Thêm ủy nhiệm chi" });
  await order.getByLabel("Số ủy nhiệm chi").fill(`UNC-${code}`);
  await order.getByRole("button", { name: "Lưu" }).click();
  await expectToast(buyer, "Đã thêm ủy nhiệm chi");
  await expect(buyer.getByText("Đã thanh toán").first()).toBeVisible();

  // 6. Thanh lý: bảng đối chiếu → lập → ký → hợp đồng Đã thanh lý.
  await buyer.goto(`/thanh-toan/thanh-ly/tao-moi?contract=${contractId}`);
  await expect(buyer.getByText("Còn phải thanh toán")).toBeVisible();
  await buyer.getByRole("button", { name: "Lập biên bản thanh lý" }).click();
  const liquidationHeading = buyer.getByRole("heading", {
    name: /^Biên bản thanh lý TL-/,
  });
  await expect(liquidationHeading).toBeVisible({ timeout: 60_000 });
  const liquidationNumber = (await liquidationHeading.textContent())!
    .replace("Biên bản thanh lý ", "")
    .trim();
  await buyer.getByRole("button", { name: "Gửi ký" }).click();
  await expectToast(buyer, "Đã gửi ký");
  await reviewAndSign(
    reviewer,
    supplier,
    director,
    clerk,
    `Biên bản thanh lý hợp đồng ${liquidationNumber}`,
  );

  await buyer.goto(`/hop-dong/${contractId}`);
  await expect(buyer.getByText("Đã thanh lý").first()).toBeVisible();
});

/** Rà soát đồng ý → NCC ký → Giám đốc ký → Văn thư đóng dấu (WF_ACCEPTANCE / WF_VALUATION / WF_LIQUIDATION). */
async function reviewAndSign(
  reviewer: Page,
  supplier: Page,
  director: Page,
  clerk: Page,
  title: string,
) {
  const dialog = await openFromInbox(reviewer, "", title);
  await dialog.getByRole("button", { name: /Đồng ý$/ }).click();
  await reviewer
    .getByRole("dialog", { name: "Đồng ý / duyệt" })
    .getByRole("button", { name: "Xác nhận" })
    .click();
  await expectToast(reviewer, "Đã xác nhận");
  await signFromInbox(supplier, "/ncc", title, /NCC ký số/);
  await signFromInbox(director, "", title, /Ký số/);
  await signFromInbox(clerk, "", title, /Đóng dấu điện tử/);
}

/** HIS (external system): POST with X-Api-Key, X-Timestamp, X-Signature = hex(HMAC_SHA256(secret, canonical)). */
async function pushReceipt(baseURL: string, payload: object) {
  const body = JSON.stringify(payload);
  const path = "/api/integration/his/warehouse-receipts";
  const timestamp = Math.floor(Date.now() / 1000).toString();
  const bodyHash = createHash("sha256").update(body, "utf8").digest("hex");
  const signature = createHmac("sha256", HIS_SECRET)
    .update(["POST", path, timestamp, bodyHash].join("\n"), "utf8")
    .digest("hex");
  const ctx = await request.newContext({ baseURL });
  const response = await ctx.post(path, {
    data: body,
    headers: {
      "Content-Type": "application/json",
      "X-Api-Key": HIS_KEY,
      "X-Timestamp": timestamp,
      "X-Signature": signature,
    },
  });
  expect(response.ok(), await response.text()).toBeTruthy();
  return (await response.json()) as {
    receiptId: string;
    validationStatus: string;
  };
}

type Item = {
  itemCode: string;
  itemName: string;
  unit: string;
  unitPrice: number;
  vatRate: number;
};

/** Setup: an effective non-bidding service contract of NCC3 with one line (the contract flow itself is E2E #1). */
async function prepareContract(
  baseURL: string,
  number: string,
  code: string,
  today: string,
) {
  const buyer = await apiAs(baseURL, "muasam");
  const json = async <T>(path: string) =>
    (await (await buyer.ctx.get(path, { headers: buyer.headers })).json()) as T;
  const categories = await json<{ id: string; code: string }[]>(
    "/api/items/categories",
  );
  const templates = await json<{ id: string; code: string }[]>(
    "/api/templates?kind=Contract",
  );
  const suppliers = await json<{ id: string }[]>(
    `/api/lookups/suppliers?search=${encodeURIComponent("Hồng Lam")}`,
  );
  const created = await buyer.ctx.post("/api/contracts", {
    headers: buyer.headers,
    data: {
      procurementType: "NonBidding",
      bidDecisionId: null,
      supplierId: suppliers[0].id,
      newSupplier: null,
      number,
      name: "Bảo dưỡng máy phát điện dự phòng (E2E #3)",
      kind: "LumpSum",
      requiresWarehouseReceipt: true,
      categoryId: categories.find((c) => c.code === "SUA_CHUA")!.id,
      facilityIds: [],
      note: null,
      signedDate: today,
      effectiveDate: today,
      expiryDate: `${Number(today.slice(0, 4)) + 1}${today.slice(4)}`,
      duration: "30 ngày",
      paymentTerms: "Thanh toán sau nghiệm thu",
      warrantyTerms: "Bảo hành 03 tháng",
      deliveryTerms: null,
      guaranteeNumber: null,
      guaranteeBank: null,
      guaranteeAmount: null,
      guaranteeExpiry: null,
      templateId: templates.find((t) => t.code === "HD_DICHVU_KHONGTHAU")!.id,
      items: [
        {
          itemId: null,
          newItem: {
            code: `E2E-${code}`,
            name: "Bảo dưỡng máy phát điện 250kVA",
            unit: "Lần",
            activeIngredient: null,
            specification: null,
            manufacturer: null,
            country: null,
            hisKey: null,
          },
          quantity: 1,
          unitPrice: 12_000_000,
          vatRate: 8,
        },
      ],
    },
  });
  expect(created.ok(), await created.text()).toBeTruthy();
  const contractId = (await created.json()) as string;
  expect(
    (
      await buyer.ctx.post(`/api/contracts/${contractId}/submit`, {
        headers: buyer.headers,
      })
    ).ok(),
  ).toBeTruthy();
  const contract = await json<{ documentId: string }>(
    `/api/contracts/${contractId}`,
  );
  await apiAct(baseURL, "rasoat", contract.documentId);
  const supplier = await apiAs(baseURL, "ncc3");
  expect(
    (
      await supplier.ctx.post(
        `/api/contracts/${contractId}/supplier-response`,
        { headers: supplier.headers, data: { agree: true, comment: null } },
      )
    ).ok(),
  ).toBeTruthy();
  for (const user of ["ncc3", "ketoan", "giamdoc", "vanthu"]) {
    await apiAct(baseURL, user, contract.documentId);
  }

  const effective = await json<{ status: string; items: Item[] }>(
    `/api/contracts/${contractId}`,
  );
  expect(effective.status).toBe("Effective");
  return { contractId, item: effective.items[0] };
}

async function apiAct(baseURL: string, user: string, documentId: string) {
  const { ctx, headers } = await apiAs(baseURL, user);
  const wf = (await (
    await ctx.get(`/api/workflows/by-document/${documentId}`, { headers })
  ).json()) as {
    myStepId: string;
    steps: { id: string; requiresSignature: boolean }[];
  };
  const step = wf.steps.find((s) => s.id === wf.myStepId)!;
  const response = step.requiresSignature
    ? await ctx.post(`/api/signing/steps/${step.id}/sign`, {
        headers,
        data: { provider: "MOCK", clientCertificate: null, comment: null },
      })
    : await ctx.post(`/api/workflows/steps/${step.id}/approve`, {
        headers,
        data: { comment: "Đồng ý", skipNextOptional: false },
      });
  expect(response.ok(), await response.text()).toBeTruthy();
}
