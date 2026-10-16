import dayjs, { APP_TIME_ZONE } from '../i18n/dayjs';
import type { Tone } from '../components/StatusTag';
import { formatDate } from './format';

/**
 * Deadline of a workflow step as the inbox shows it (design/Trinh ky.dc.html): "Hạn 17/10/2026",
 * "Hạn 16/10/2026 · hôm nay", "Quá hạn 2 ngày". Days are counted on Vietnam calendar dates.
 */
export function dueLabel(
  dueAt: string | null | undefined,
  overdue: boolean,
  now: Date = new Date(),
): { text: string; tone: Tone } {
  if (!dueAt) return { text: 'Không có hạn', tone: 'neutral' };
  const due = dayjs(dueAt).tz(APP_TIME_ZONE).startOf('day');
  const today = dayjs(now).tz(APP_TIME_ZONE).startOf('day');
  const days = due.diff(today, 'day');
  if (overdue || days < 0) {
    const late = Math.max(0, -days);
    return { text: late > 0 ? `Quá hạn ${late} ngày` : 'Quá hạn hôm nay', tone: 'danger' };
  }
  if (days === 0) return { text: `Hạn ${formatDate(dueAt)} · hôm nay`, tone: 'warning' };
  if (days === 1) return { text: `Hạn ${formatDate(dueAt)} · ngày mai`, tone: 'warning' };
  return { text: `Hạn ${formatDate(dueAt)}`, tone: 'neutral' };
}
