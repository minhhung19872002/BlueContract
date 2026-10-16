import type { ReactNode } from 'react';
import type { Tone } from './StatusTag';
import './barChart.css';

export interface HBar {
  key: string;
  label: ReactNode;
  value: number;
  /** Shown on the right (formatted value). */
  display: ReactNode;
  tone?: Tone;
}

/**
 * Horizontal ranking bars (design/Bao cao.dc.html "Top nhà cung cấp theo công nợ", "Thời gian ký theo bước"):
 * label, bar scaled to the largest value, formatted value on the right.
 */
export function HBarChart({
  bars,
  label,
  empty = 'Chưa có số liệu',
}: {
  bars: HBar[];
  label: string;
  empty?: string;
}) {
  const max = Math.max(0, ...bars.map((b) => b.value));
  if (bars.length === 0) return <div className="hbar-chart__empty">{empty}</div>;
  return (
    <ul className="hbar-chart" aria-label={label}>
      {bars.map((b) => (
        <li key={b.key} className="hbar-chart__row">
          <span className="hbar-chart__label">{b.label}</span>
          <span className="hbar-chart__track" aria-hidden>
            <span
              style={{
                width: `${max > 0 ? Math.max(1, (b.value / max) * 100) : 0}%`,
                background: `var(--bp-${b.tone ?? 'primary'}-solid)`,
              }}
            />
          </span>
          <span className="hbar-chart__value">{b.display}</span>
        </li>
      ))}
    </ul>
  );
}
