import { expect, test, type Browser, type Page } from '@playwright/test';
import { PASSWORD } from './helpers';

/**
 * AUDIT-1 §12.7 / spec §9.3 "Trình ký dùng được trên máy tính bảng và điện thoại": the signing inbox (table and quick
 * view), the document dialog and the supplier inbox never scroll sideways at 375×812 (phone) and 768×1024 (tablet).
 */
const VIEWPORTS = [
  { name: 'điện thoại 375×812', width: 375, height: 812, isMobile: true, hasTouch: true },
  { name: 'máy tính bảng 768×1024', width: 768, height: 1024, isMobile: false, hasTouch: true },
];

async function loginOn(browser: Browser, userName: string, v: (typeof VIEWPORTS)[number]): Promise<Page> {
  const context = await browser.newContext({
    viewport: { width: v.width, height: v.height },
    isMobile: v.isMobile,
    hasTouch: v.hasTouch,
    deviceScaleFactor: 2,
  });
  const page = await context.newPage();
  await page.goto('/dang-nhap');
  await page.getByLabel('Tên đăng nhập').fill(userName);
  await page.getByLabel('Mật khẩu').fill(PASSWORD);
  await page.getByRole('button', { name: 'Đăng nhập' }).click();
  await expect(page).not.toHaveURL(/dang-nhap/);
  return page;
}

/** The page itself (not an inner scroller such as a wide table) must fit the viewport. */
async function expectNoSideScroll(page: Page, where: string) {
  await page.waitForTimeout(300); // layout after fonts / transitions
  const { scrollWidth, innerWidth, widest } = await page.evaluate(() => {
    const doc = document.documentElement;
    // The element sticking out furthest, to make a failure easy to fix.
    let widest = '';
    let right = window.innerWidth;
    for (const el of Array.from(document.body.querySelectorAll<HTMLElement>('*'))) {
      const r = el.getBoundingClientRect();
      if (r.width > 0 && r.right > right + 1 && getComputedStyle(el).position !== 'fixed') {
        right = r.right;
        widest = `${el.tagName.toLowerCase()}.${(el.className || '').toString().split(' ').slice(0, 3).join('.')} → ${Math.round(r.right)}px`;
      }
    }
    return { scrollWidth: doc.scrollWidth, innerWidth: window.innerWidth, widest };
  });
  expect(scrollWidth, `${where}: page wider than the screen (${widest})`).toBeLessThanOrEqual(innerWidth + 1);
}

for (const v of VIEWPORTS) {
  test(`Trình ký không cuộn ngang trên ${v.name}`, async ({ browser }) => {
    const director = await loginOn(browser, 'giamdoc', v);

    await director.goto('/trinh-ky');
    await expect(director.getByText('Chờ tôi xử lý').first()).toBeVisible();
    await expectNoSideScroll(director, 'Trình ký (bảng)');

    await director.goto('/trinh-ky?view=quick');
    await expect(director.locator('main')).toBeVisible();
    await expectNoSideScroll(director, 'Trình ký (xem nhanh)');

    // The document dialog of the first waiting item.
    await director.goto('/trinh-ky');
    const first = director.locator('.ant-table-tbody tr.ant-table-row').first();
    if (await first.isVisible()) {
      await first.click();
      await expect(director.getByRole('dialog', { name: 'Xem và xử lý tài liệu' })).toBeVisible();
      await expectNoSideScroll(director, 'Hộp thoại tài liệu');
    }

    const supplier = await loginOn(browser, 'ncc1', v);
    await supplier.goto('/ncc/trinh-ky');
    await expect(supplier.locator('main')).toBeVisible();
    await expectNoSideScroll(supplier, 'Trình ký Cổng NCC');
  });
}
