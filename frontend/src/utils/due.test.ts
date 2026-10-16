import { describe, expect, it } from 'vitest';
import { dueLabel } from './due';

// 16/10/2026 10:00 in Vietnam.
const now = new Date('2026-10-16T03:00:00Z');

describe('dueLabel', () => {
  it('counts days on Vietnam calendar dates', () => {
    expect(dueLabel('2026-10-19T09:00:00Z', false, now)).toEqual({ text: 'Hạn 19/10/2026', tone: 'neutral' });
    // 17/10 06:00 VN = 16/10 23:00 UTC: tomorrow in Vietnam, today in UTC.
    expect(dueLabel('2026-10-16T23:00:00Z', false, now)).toEqual({
      text: 'Hạn 17/10/2026 · ngày mai',
      tone: 'warning',
    });
    expect(dueLabel('2026-10-16T15:00:00Z', false, now)).toEqual({
      text: 'Hạn 16/10/2026 · hôm nay',
      tone: 'warning',
    });
  });

  it('shows how late an overdue step is', () => {
    expect(dueLabel('2026-10-14T09:00:00Z', true, now)).toEqual({ text: 'Quá hạn 2 ngày', tone: 'danger' });
    expect(dueLabel('2026-10-16T01:00:00Z', true, now)).toEqual({ text: 'Quá hạn hôm nay', tone: 'danger' });
    expect(dueLabel(null, false, now).text).toBe('Không có hạn');
  });
});
