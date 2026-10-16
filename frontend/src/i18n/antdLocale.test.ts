import { describe, expect, it } from 'vitest';
import { antdLocale } from './antdLocale';

describe('antdLocale', () => {
  it('keeps the Vietnamese texts of every antd component', () => {
    expect(antdLocale.locale).toBe('vi');
    expect(antdLocale.Modal?.cancelText).toBe('Hủy');
    expect(antdLocale.Popconfirm?.okText).toBe('Đồng ý');
    expect(antdLocale.DatePicker?.lang.placeholder).toBe('Chọn ngày');
    expect(antdLocale.Pagination?.items_per_page).toBe('/ trang');
    expect(antdLocale.Table?.emptyText).toBeTruthy();
  });
});
