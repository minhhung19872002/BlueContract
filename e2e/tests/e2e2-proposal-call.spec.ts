import { expect, test } from '@playwright/test';
import { chooseOption, expectToast, loginAs, signFromInbox } from './helpers';

/**
 * E2E #2 (spec §13): Đề xuất → tổng hợp → GĐ ký → gọi hàng → NCC xác nhận, through the UI with khoanoi, tonghopvt,
 * totruongvattu, giamdoc and ncc2 (the supplier of the effective VTYT demo contract).
 */
test('proposal, summary, director approval, call order and supplier confirmation', async ({ browser }) => {
  // 1. Khoa Nội lập phiếu đề xuất vật tư (X–N–T từ HIS), ký và gửi.
  const department = await loginAs(browser, 'khoanoi');
  await department.goto('/de-xuat/tao-moi');
  await chooseOption(department, department, 'Mảng', 'Vật tư y tế');
  await chooseOption(department, department, 'Kho (lấy Xuất – Nhập – Tồn)', 'Kho Vật tư y tế');
  const rows = department.locator('.ant-table-tbody tr.ant-table-row');
  await expect(rows.first()).toBeVisible();
  await rows.nth(0).locator('input.ant-input-number-input').fill('12');
  await rows.nth(1).locator('input.ant-input-number-input').fill('8');
  await department.getByRole('button', { name: 'Lưu phiếu' }).click();
  const proposalHeading = department.getByRole('heading', { name: /^Phiếu đề xuất DX-/ });
  await expect(proposalHeading).toBeVisible({ timeout: 60_000 });
  const proposalNumber = (await proposalHeading.textContent())!.replace('Phiếu đề xuất ', '').trim();
  await department.getByRole('button', { name: 'Ký và gửi' }).click();
  await expectToast(department, 'Đã gửi ký');
  await department.getByRole('button', { name: /Ký số/ }).first().click();
  await expectToast(department, 'Đã ký số thành công');
  await expect(department.getByText('Đã gửi — chờ tổng hợp')).toBeVisible();

  // 2. Người tổng hợp gộp phiếu, ký và gửi duyệt.
  const aggregator = await loginAs(browser, 'tonghopvt');
  await aggregator.goto('/tong-hop-de-xuat/tao-moi');
  await aggregator.locator('form .ant-select').first().click();
  await aggregator.locator('.ant-select-dropdown:visible .ant-select-item-option', { hasText: 'Vật tư y tế' }).click();
  await aggregator.getByRole('row', { name: new RegExp(proposalNumber) }).locator('input[type=checkbox]').check();
  await aggregator.getByRole('button', { name: /Tổng hợp \(1 phiếu\)/ }).click();
  const summaryHeading = aggregator.getByRole('heading', { name: /^Phiếu tổng hợp TH-/ });
  await expect(summaryHeading).toBeVisible({ timeout: 60_000 });
  const summaryUrl = aggregator.url();
  const summaryNumber = (await summaryHeading.textContent())!.replace('Phiếu tổng hợp ', '').trim();
  await aggregator.getByRole('button', { name: 'Gửi ký duyệt' }).click();
  await expectToast(aggregator, 'Đã gửi ký duyệt');
  await aggregator.getByRole('button', { name: /Ký số/ }).first().click();
  await expectToast(aggregator, 'Đã ký số thành công');

  // 3. Tổ trưởng vật tư duyệt; 4. Giám đốc ký.
  const head = await loginAs(browser, 'totruongvattu');
  await head.goto(summaryUrl);
  await expect(head.getByText('Tổ trưởng duyệt').first()).toBeVisible();
  await head.getByRole('button', { name: /Duyệt$/ }).click();
  await head.getByRole('dialog', { name: 'Đồng ý / duyệt' }).getByRole('button', { name: 'Xác nhận' }).click();
  await expectToast(head, 'Đã xác nhận');
  const director = await loginAs(browser, 'giamdoc');
  await signFromInbox(director, '', `Phiếu tổng hợp đề xuất ${summaryNumber}`, /Ký số/);

  // 5. Gọi hàng toàn bộ số còn lại → 1 đơn cho NCC2.
  await aggregator.goto(summaryUrl);
  await expect(aggregator.getByText('Đã duyệt').first()).toBeVisible();
  await aggregator.getByRole('button', { name: 'Gọi hàng' }).click();
  await chooseOption(aggregator, aggregator, 'Kho nhận', 'Kho Vật tư y tế');
  await aggregator.getByRole('button', { name: 'Gọi toàn bộ số còn lại' }).click();
  await aggregator.getByRole('button', { name: /Gọi hàng \(1 NCC\)/ }).click();
  await expectToast(aggregator, 'Đã tạo 1 đơn gọi hàng');
  await expect(aggregator.getByRole('heading', { name: /^Đơn gọi hàng GH-/ })).toBeVisible();
  const orderId = aggregator.url().split('/').pop()!;

  // 6. NCC2 xác nhận thiếu một mặt hàng (bắt buộc lý do).
  const supplier = await loginAs(browser, 'ncc2');
  await supplier.goto(`/ncc/goi-hang/${orderId}`);
  const lines = supplier.locator('.ant-table-tbody tr.ant-table-row');
  await lines.nth(0).locator('input.ant-input-number-input').fill('5');
  await expect(supplier.getByRole('button', { name: 'Gửi xác nhận' })).toBeDisabled();
  await lines.nth(0).locator('input.ant-input:not(.ant-input-number-input)').fill('Hàng về không đủ, giao bổ sung đợt sau');
  await supplier.getByRole('button', { name: 'Gửi xác nhận' }).click();
  await expectToast(supplier, 'Đã gửi xác nhận');
  await expect(supplier.getByText('Xác nhận một phần').first()).toBeVisible();

  // 7. Bệnh viện thấy trạng thái xác nhận.
  await aggregator.reload();
  await expect(aggregator.getByText('Xác nhận một phần').first()).toBeVisible();
  await expect(aggregator.getByText('Hàng về không đủ, giao bổ sung đợt sau')).toBeVisible();
});
