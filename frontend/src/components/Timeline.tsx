import { Empty } from 'antd';
import type { ReactNode } from 'react';
import type { Tone } from './StatusTag';
import './timeline.css';

/** "Nguyễn Văn An" → "VA"; empty → initials of "Hệ thống". */
export const initials = (name?: string | null) => {
  const parts = (name ?? 'Hệ thống').trim().split(/\s+/).filter(Boolean);
  return ((parts.length > 1 ? (parts.at(-2)?.[0] ?? '') : '') + (parts.at(-1)?.[0] ?? '')).toUpperCase();
};

export interface TimelineItem {
  key: string | number;
  tone: Tone;
  /** Avatar content; defaults to the initials of `who`. */
  marker?: ReactNode;
  /** Who acted ("Hệ thống" when empty). */
  who?: string | null;
  /** Kind of event (StatusTag…). */
  tag?: ReactNode;
  /** After the tag, in caption style (step, document…). */
  context?: ReactNode;
  /** Already formatted time. */
  at?: string;
  /** Extra caption lines. */
  lines?: ReactNode[];
  /** Free text (reason, comment) in a soft box. */
  note?: string | null;
}

/**
 * Avatar timeline (design "Lịch sử / Ý kiến"): who, what, when, note. Shared by the signing workflow history and the
 * timelines of business documents (đơn gọi hàng…).
 */
export function Timeline({ items, empty = 'Chưa có diễn biến' }: { items: TimelineItem[]; empty?: string }) {
  if (items.length === 0) return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={empty} />;
  return (
    <div className="timeline">
      {items.map((e, i) => (
        <div key={e.key} className="timeline__item">
          <div className="timeline__rail">
            <span
              className="timeline__avatar"
              style={{ background: `var(--bp-${e.tone}-bg)`, color: `var(--bp-${e.tone}-fg)` }}
            >
              {e.marker ?? initials(e.who)}
            </span>
            {i < items.length - 1 && <span className="timeline__line" />}
          </div>
          <div className="timeline__body">
            <div className="timeline__head">
              <span className="timeline__who">{e.who ?? 'Hệ thống'}</span>
              {e.tag}
              {e.context && <span className="timeline__context">· {e.context}</span>}
            </div>
            {e.at && <div className="timeline__at">{e.at}</div>}
            {e.lines?.map((line, j) => (
              <div key={j} className="timeline__at">
                {line}
              </div>
            ))}
            {e.note && <div className="timeline__note">{e.note}</div>}
          </div>
        </div>
      ))}
    </div>
  );
}
