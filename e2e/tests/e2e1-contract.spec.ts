import { expect, test } from '@playwright/test';
import {
  apiAs,
  chooseOption,
  expectToast,
  fillDate,
  loginAs,
  openFromInbox,
  signFromInbox,
  withTrackedChanges,
} from './helpers';

/**
 * E2E #1 (spec §13): Tạo QĐ → HĐ → NCC sửa → Kế toán duyệt → ký đủ → hiệu lực — entirely through the UI,
 * with the roles muasam, rasoat, ncc1, ketoan, giamdoc, vanthu.
 */
test('bid decision to effective contract with the supplier revision loop', async ({ browser, baseURL }) => {
  const suffix = Date.now().toString().slice(-6);
  const bidNumber = `E2E-${suffix}/QĐ-BV`;
  const contractNumber = `E2E-${suffix}/HĐ-BV`;
  const title = `Hợp đồng số ${contractNumber}`;

  // 1. Tổ Mua sắm: quyết định thầu, NCC trúng thầu, danh mục từ HIS, ban hành.
  const procurement = await loginAs(browser, 'muasam');
  await procurement.goto('/quyet-dinh-thau');
  await procurement.getByRole('button', { name: 'Tạo quyết định thầu' }).click();
  const bidDialog = procurement.getByRole('dialog', { name: 'Tạo quyết định thầu' });
  await bidDialog.getByLabel('Số quyết định').fill(bidNumber);
  await fillDate(bidDialog, 'Ngày ký', '15/02/2026');
  await chooseOption(procurement, bidDialog, 'Hạng mục', 'Thuốc');
  await bidDialog.getByLabel('Tên gói thầu').fill('Gói thầu E2E: Thuốc generic');
  await bidDialog.getByLabel('Trích yếu').fill('Phê duyệt kết quả lựa chọn nhà thầu (kiểm thử E2E)');
  await bidDialog.getByRole('button', { name: 'Tạo' }).click();
  await expect(procurement).toHaveURL(/quyet-dinh-thau\/[0-9a-f-]+$/);
  await expect(procurement.getByRole('heading', { name: `Quyết định ${bidNumber}` })).toBeVisible();

  const supplierSelect = procurement.locator('form.ant-form-inline .ant-select').first();
  await supplierSelect.click();
  await supplierSelect.locator('input').pressSequentially('0101234567');
  await procurement.locator('.ant-select-dropdown:visible .ant-select-item-option', { hasText: '0101234567' }).first().click();
  await procurement.getByRole('button', { name: 'Thêm / cập nhật NCC' }).click();
  await expectToast(procurement, 'Đã cập nhật NCC trúng thầu');
  await procurement.getByRole('button', { name: 'Đồng bộ HIS tất cả NCC' }).click();
  await expect(procurement.getByText(/Thêm mới \d+, cập nhật/)).toBeVisible();
  await procurement.getByRole('button', { name: 'OK' }).click();
  await procurement.getByRole('button', { name: 'Ban hành' }).click();
  await procurement.getByRole('button', { name: 'OK' }).click();
  await expect(procurement.getByText('Đã ban hành', { exact: true })).toBeVisible();

  // 2. Wizard hợp đồng: chọn 2 mặt hàng, nhập số HĐ, mẫu mặc định, tạo.
  await procurement.getByRole('button', { name: 'Tạo hợp đồng' }).click();
  await expect(procurement).toHaveURL(/hop-dong\/tao-moi/);
  await procurement.getByRole('button', { name: 'Tiếp tục' }).click();
  const rows = procurement.locator('.ant-table-tbody tr.ant-table-row');
  await expect(rows.first()).toBeVisible();
  await rows.nth(0).locator('input[type=checkbox]').check();
  await rows.nth(1).locator('input[type=checkbox]').check();
  await expect(procurement.getByText('Số mặt hàng')).toBeVisible();
  await procurement.getByRole('button', { name: 'Tiếp tục' }).click();
  await procurement.getByLabel('Số hợp đồng').fill(contractNumber);
  await procurement.getByRole('button', { name: 'Tiếp tục' }).click();
  await procurement.getByRole('button', { name: 'Xem trước PDF' }).click();
  await expect(procurement.locator('.pdf-viewer__pages')).toHaveText(/^Trang \d+ \/ [1-9]\d*$/, { timeout: 90_000 });
  await procurement.getByRole('button', { name: 'Tạo hợp đồng' }).click();
  await expect(procurement.getByRole('heading', { name: `Hợp đồng ${contractNumber}` })).toBeVisible({ timeout: 90_000 });
  const contractUrl = procurement.url();
  const contractId = contractUrl.split('/').pop()!;

  await procurement.getByRole('button', { name: 'Gửi rà soát' }).click();
  await procurement.getByRole('button', { name: 'OK' }).click();
  await expect(procurement.getByText('Tổ rà soát kiểm tra').first()).toBeVisible();

  // 3. Tổ rà soát đồng ý → gửi NCC.
  const reviewer = await loginAs(browser, 'rasoat');
  const reviewDialog = await openFromInbox(reviewer, '', title);
  await reviewDialog.getByRole('button', { name: 'Đồng ý' }).click();
  await reviewer.getByRole('dialog', { name: 'Đồng ý / duyệt' }).getByRole('button', { name: 'Xác nhận' }).click();
  await expectToast(reviewer, 'Đã xác nhận');

  // 4. NCC: tải lên bản sửa (Track Changes) và gửi lại.
  const supplier = await loginAs(browser, 'ncc1');
  await supplier.goto(`/ncc/hop-dong/${contractId}`);
  await expect(supplier.getByText('Chờ Quý công ty rà soát')).toBeVisible();
  const api = await apiAs(baseURL!, 'ncc1');
  const contract = (await (await api.ctx.get(`/api/contracts/${contractId}`, { headers: api.headers })).json()) as { documentId: string };
  const doc = (await (await api.ctx.get(`/api/documents/${contract.documentId}`, { headers: api.headers })).json()) as {
    versions: { id: string; isCurrent: boolean }[];
  };
  const current = doc.versions.find((v) => v.isCurrent)!;
  const docx = await (await api.ctx.get(`/api/documents/${contract.documentId}/versions/${current.id}/file?format=docx`, { headers: api.headers })).body();
  const revised = await withTrackedChanges(docx, 'Giao hàng trong 03 ngày làm việc.');
  await supplier.locator('input[type=file][accept=".docx"]').setInputFiles({
    name: 'hop-dong-ncc-sua.docx',
    mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    buffer: revised,
  });
  await expectToast(supplier, 'Đã tải lên bản đề xuất sửa đổi');
  await expect(supplier.getByText('2 đề xuất sửa đổi đang theo dõi')).toBeVisible();
  await supplier.getByRole('button', { name: 'Gửi lại (có sửa đổi)' }).click();
  const sendBack = supplier.getByRole('dialog', { name: 'Gửi lại hợp đồng cho Bệnh viện' });
  await sendBack.getByLabel('Ý kiến gửi kèm').fill('Đề nghị rút ngắn thời gian giao hàng');
  await sendBack.getByRole('button', { name: 'Gửi lại' }).click();
  await expectToast(supplier, 'Đã gửi lại');

  // 5. Kế toán: từ chối thay đổi xóa, chấp nhận thay đổi thêm, gửi lại NCC xác nhận.
  const accountant = await loginAs(browser, 'ketoan');
  await accountant.goto(`/hop-dong/${contractId}`);
  await expect(accountant.getByText('Thay đổi do nhà cung cấp đề xuất')).toBeVisible();
  const revisionRows = accountant.locator('.ant-card').filter({ hasText: 'Thay đổi do nhà cung cấp đề xuất' }).locator('tbody tr.ant-table-row');
  await expect(revisionRows).toHaveCount(2);
  await revisionRows.nth(0).getByText('Từ chối').click();
  await revisionRows.nth(1).getByText('Chấp nhận').click();
  await accountant.getByRole('button', { name: 'Áp dụng (2)' }).click();
  await expectToast(accountant, 'Đã áp dụng quyết định');
  await expect(accountant.getByText('Không còn thay đổi nào cần xử lý')).toBeVisible();
  await accountant.getByRole('button', { name: 'Gửi lại NCC xác nhận' }).click();
  const returnDialog = accountant.getByRole('dialog', { name: 'Gửi lại nhà cung cấp' });
  await returnDialog.getByLabel('Ý kiến của Kế toán').fill('Chấp nhận rút ngắn thời gian giao hàng, giữ nguyên điều khoản còn lại.');
  await returnDialog.getByRole('button', { name: 'Gửi lại NCC' }).click();
  await expectToast(accountant, 'Đã gửi lại NCC');

  // 6. NCC đồng ý bản cuối → chốt bản → NCC ký số.
  await supplier.reload();
  await supplier.getByRole('button', { name: 'Đồng ý ký' }).click();
  await expectToast(supplier, 'Đã đồng ý');
  await signFromInbox(supplier, '/ncc', title, /NCC ký số/);

  // 7. Kế toán ký nháy → Giám đốc ký → Văn thư đóng dấu.
  await signFromInbox(accountant, '', title, /Ký nháy/);
  const director = await loginAs(browser, 'giamdoc');
  await signFromInbox(director, '', title, /Ký số/);
  const clerk = await loginAs(browser, 'vanthu');
  await signFromInbox(clerk, '', title, /Đóng dấu điện tử/);

  // 8. Hợp đồng có hiệu lực, 4 chữ ký hợp lệ.
  await procurement.goto(contractUrl);
  await expect(procurement.getByText('Đang hiệu lực').first()).toBeVisible();
  await procurement.getByRole('tab', { name: 'Luồng ký' }).click();
  await expect(procurement.getByText('Chữ ký số (4)')).toBeVisible();
});
