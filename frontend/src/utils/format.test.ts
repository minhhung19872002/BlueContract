import { formatDate, formatDateTime, formatMoney, formatQuantity } from './format';

describe('formatMoney', () => {
  it('uses dot thousands separator and đồng sign', () => {
    expect(formatMoney(1234567)).toBe('1.234.567 ₫');
  });

  it('rounds to whole đồng', () => {
    expect(formatMoney(999.6)).toBe('1.000 ₫');
  });

  it('returns empty string for missing values', () => {
    expect(formatMoney(null)).toBe('');
    expect(formatMoney(undefined)).toBe('');
  });
});

describe('formatQuantity', () => {
  it('keeps up to 3 decimals with comma', () => {
    expect(formatQuantity(1234.5)).toBe('1.234,5');
    expect(formatQuantity(0.125)).toBe('0,125');
  });
});

describe('formatDate', () => {
  it('converts UTC to Vietnam date (UTC+7)', () => {
    // 2026-03-31T18:30Z is already 01/04 in Vietnam.
    expect(formatDate('2026-03-31T18:30:00Z')).toBe('01/04/2026');
  });

  it('formats date time in Vietnam time', () => {
    expect(formatDateTime('2026-03-31T18:30:00Z')).toBe('01/04/2026 01:30');
  });
});
