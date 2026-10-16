import { expect, request, type Browser, type Locator, type Page } from '@playwright/test';
import JSZip from 'jszip';

export const PASSWORD = process.env.DEMO_PASSWORD ?? 'Demo@123';

/** A fresh browser context per user, so each role has its own session (refresh cookie). */
export async function loginAs(browser: Browser, userName: string): Promise<Page> {
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.goto('/dang-nhap');
  await page.getByLabel('Tên đăng nhập').fill(userName);
  await page.getByLabel('Mật khẩu').fill(PASSWORD);
  await page.getByRole('button', { name: 'Đăng nhập' }).click();
  await expect(page).not.toHaveURL(/dang-nhap/);
  return page;
}

/** Picks an option of an Ant Design Select identified by its form label (types to filter searchable selects). */
export async function chooseOption(page: Page, scope: Page | Locator, label: string, option: string | RegExp, search?: string) {
  const input = scope.getByLabel(label, { exact: true });
  await input.click({ force: true });
  if (search) await input.pressSequentially(search);
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option', { hasText: option }).first().click();
}
export async function fillDate(scope: Page | Locator, label: string, value: string) {
  const input = scope.getByLabel(label, { exact: true });
  await input.click();
  await input.fill(value);
  await input.press('Enter');
}

export async function expectToast(page: Page, text: string | RegExp) {
  await expect(page.locator('.ant-message-notice').filter({ hasText: text }).first()).toBeVisible();
}

/** Calls the API as a user (used only to fetch files the UI downloads, never to drive the flow). */
export async function apiAs(baseURL: string, userName: string) {
  const ctx = await request.newContext({ baseURL });
  const login = await ctx.post('/api/auth/login', { data: { userName, password: PASSWORD } });
  expect(login.ok()).toBeTruthy();
  const { accessToken } = (await login.json()) as { accessToken: string };
  return { ctx, headers: { Authorization: `Bearer ${accessToken}` } };
}

/** Simulates the supplier editing with Track Changes: deletes the first sentence run and inserts a new sentence. */
export async function withTrackedChanges(docx: Buffer, inserted: string): Promise<Buffer> {
  const zip = await JSZip.loadAsync(docx);
  const file = zip.file('word/document.xml');
  if (!file) throw new Error('document.xml missing');
  let xml = await file.async('string');
  const run = /<w:r\b[^>]*>(?:(?!<\/w:r>)[\s\S])*?<w:t\b[^>]*>[^<]{4,}<\/w:t>(?:(?!<\/w:r>)[\s\S])*?<\/w:r>/.exec(xml);
  if (!run) throw new Error('no text run found');
  const deleted = run[0].replace(/<w:t\b/g, '<w:delText').replace(/<\/w:t>/g, '</w:delText>');
  const stamp = 'w:author="Nhà cung cấp" w:date="2026-10-16T08:00:00Z"';
  xml = xml.replace(
    run[0],
    `<w:del w:id="9001" ${stamp}>${deleted}</w:del><w:ins w:id="9002" ${stamp}><w:r><w:t xml:space="preserve">${inserted}</w:t></w:r></w:ins>`,
  );
  zip.file('word/document.xml', xml);
  return zip.generateAsync({ type: 'nodebuffer' });
}

/** Opens a document from the "Trình ký" inbox (Chờ tôi xử lý) by its title. */
export async function openFromInbox(page: Page, base: '' | '/ncc', title: string) {
  await page.goto(`${base}/trinh-ky`);
  await page.getByRole('cell', { name: title }).first().click();
  await expect(page.getByRole('dialog', { name: 'Xem và xử lý tài liệu' })).toBeVisible();
  return page.getByRole('dialog', { name: 'Xem và xử lý tài liệu' });
}

/** Clicks the main signing button of the active step (provider = default MOCK) and waits for success. */
export async function signFromInbox(page: Page, base: '' | '/ncc', title: string, label: RegExp) {
  const dialog = await openFromInbox(page, base, title);
  await dialog.getByRole('button', { name: label }).first().click();
  await expectToast(page, 'Đã ký số thành công');
}
