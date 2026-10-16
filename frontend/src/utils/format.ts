import dayjs, { APP_TIME_ZONE } from '../i18n/dayjs';

const integerFormatter = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });
const quantityFormatter = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 });

/** 1234567 → "1.234.567 ₫" */
export function formatMoney(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '';
  return `${integerFormatter.format(Math.round(value))} ₫`;
}

/** 1234.5 → "1.234,5" (quantities keep up to 3 decimals). */
export function formatQuantity(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '';
  return quantityFormatter.format(value);
}

/** ISO/UTC timestamp or date → "dd/MM/yyyy" in Vietnam time. */
export function formatDate(value: string | Date | null | undefined): string {
  if (!value) return '';
  return dayjs(value).tz(APP_TIME_ZONE).format('DD/MM/YYYY');
}

/** ISO/UTC timestamp → "dd/MM/yyyy HH:mm" in Vietnam time. */
export function formatDateTime(value: string | Date | null | undefined): string {
  if (!value) return '';
  return dayjs(value).tz(APP_TIME_ZONE).format('DD/MM/YYYY HH:mm');
}
