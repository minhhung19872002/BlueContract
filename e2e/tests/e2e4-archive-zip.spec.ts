import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import { expect, test } from "@playwright/test";
import JSZip from "jszip";
import { expectToast, loginAs } from "./helpers";

const CONTRACT = "18/2026/HĐ-DV";

/**
 * E2E #4 (spec §13): Hồ sơ lưu trữ — tìm hợp đồng đã thanh lý, xem cây tài liệu, tải toàn bộ hồ sơ ZIP kèm muc_luc.pdf,
 * đánh dấu hồ sơ đã đầy đủ.
 */
test("archive dossier tree and full ZIP download with index", async ({
  browser,
}) => {
  const buyer = await loginAs(browser, "muasam");
  await buyer.goto("/ho-so-luu-tru");
  await buyer
    .getByPlaceholder("Số hợp đồng, số quyết định, tên NCC")
    .fill(CONTRACT);
  await buyer
    .getByPlaceholder("Số hợp đồng, số quyết định, tên NCC")
    .press("Enter");
  await buyer
    .locator(".ant-list-item")
    .filter({ hasText: CONTRACT })
    .first()
    .click();
  await expect(buyer.getByText(`Hồ sơ hợp đồng ${CONTRACT}`)).toBeVisible();

  // Cây tài liệu: các nhóm từ quyết định thầu đến thanh lý.
  for (const folder of [
    "Quyết định thầu",
    "Hợp đồng và phụ lục",
    "Phiếu nhập kho",
    "Nghiệm thu",
    "Thanh lý",
  ]) {
    await expect(
      buyer.locator(".ant-tree").getByText(folder, { exact: true }),
    ).toBeVisible();
  }
  await buyer
    .locator(".ant-tree")
    .getByText(/^Biên bản thanh lý TL-/)
    .click();
  await expect(buyer.getByText(/PDF đã ký \(\d+ chữ ký\)/)).toBeVisible();

  // Tải ZIP: mỗi giai đoạn một thư mục + muc_luc.pdf.
  const [download] = await Promise.all([
    buyer.waitForEvent("download", { timeout: 120_000 }),
    buyer.getByRole("button", { name: /Tải toàn bộ hồ sơ \(ZIP\)/ }).click(),
  ]);
  expect(download.suggestedFilename()).toMatch(/^ho-so-.*\.zip$/);
  const zip = await JSZip.loadAsync(await readFile((await download.path())!));
  const names = Object.keys(zip.files).filter((n) => !zip.files[n].dir);
  expect(names).toContain("muc_luc.pdf");
  expect(
    (await zip.file("muc_luc.pdf")!.async("nodebuffer"))
      .subarray(0, 4)
      .toString(),
  ).toBe("%PDF");
  expect(
    names.some((n) => n.startsWith("02_Hop_dong_va_phu_luc/")),
  ).toBeTruthy();
  expect(names.some((n) => n.startsWith("07_Thanh_ly/"))).toBeTruthy();
  expect(names.length).toBeGreaterThanOrEqual(6);

  // The signed liquidation PDF in the ZIP is byte-identical to the archived one (same SHA-256 as shown in the UI).
  const shown = (await buyer
    .locator(".ant-typography code")
    .first()
    .textContent())!.trim();
  const liquidation = names.find(
    (n) => n.startsWith("07_Thanh_ly/") && n.endsWith(".pdf"),
  )!;
  const packed = createHash("sha256")
    .update(await zip.file(liquidation)!.async("nodebuffer"))
    .digest("hex");
  expect(packed).toBe(shown);

  // Đánh dấu hồ sơ đã đầy đủ.
  const toggle = buyer.getByRole("switch");
  if ((await toggle.getAttribute("aria-checked")) !== "true") {
    await toggle.click();
    await expectToast(buyer, "Đã đánh dấu hồ sơ đầy đủ");
  }
  await expect(
    buyer.getByText(/Hồ sơ đã được xác nhận đầy đủ bởi/),
  ).toBeVisible();
});
