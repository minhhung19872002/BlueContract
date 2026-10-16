import type { Locale } from 'antd/es/locale';
// The ES build: "antd/locale/vi_VN" is CommonJS and, once bundled, its default import is the module wrapper
// ({ default: … }), which silently left Modal / Popconfirm / DatePicker / Table in English.
import viVN from 'antd/es/locale/vi_VN';

// antd's vi_VN date-picker locale is overwritten by rc-picker's English placeholders: restore Vietnamese ones.
/** AntD texts in Vietnamese, with the few rc-* strings antd's own vi_VN still leaves in English. */
export const antdLocale: Locale = {
  ...viVN,
  // Same issue with rc-pagination: "20 / page" instead of "20 / trang".
  Pagination: {
    ...viVN.Pagination,
    items_per_page: '/ trang',
    jump_to: 'Đến trang',
    jump_to_confirm: 'xác nhận',
    page: 'Trang',
    prev_page: 'Trang trước',
    next_page: 'Trang sau',
    prev_5: 'Lùi 5 trang',
    next_5: 'Tới 5 trang',
    prev_3: 'Lùi 3 trang',
    next_3: 'Tới 3 trang',
    page_size: 'Số dòng mỗi trang',
  },
  // Empty tables otherwise render rc-table's English "No data".
  Empty: { description: 'Không có dữ liệu' },
  DatePicker: viVN.DatePicker && {
    ...viVN.DatePicker,
    lang: {
      ...viVN.DatePicker.lang,
      placeholder: 'Chọn ngày',
      yearPlaceholder: 'Chọn năm',
      monthPlaceholder: 'Chọn tháng',
      rangePlaceholder: ['Từ ngày', 'Đến ngày'] as [string, string],
    },
  },
};
