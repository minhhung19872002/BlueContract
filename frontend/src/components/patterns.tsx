import type { ReactNode } from 'react';
import type { Tone } from './StatusTag';

/** KPI card (design/Page List.dc.html "kpis"): icon in a tone square, label, value, optional sub line and progress. */
export function KpiCard({
  icon,
  label,
  value,
  sub,
  tone = 'primary',
  percent,
  onClick,
}: {
  icon?: ReactNode;
  label: ReactNode;
  value: ReactNode;
  sub?: ReactNode;
  tone?: Tone;
  /** Shows a progress bar under the value (0–100). */
  percent?: number;
  onClick?: () => void;
}) {
  const content = (
    <>
      {icon && (
        <span
          className="kpi-card__icon"
          style={{ background: `var(--bp-${tone}-bg)`, color: `var(--bp-${tone}-fg)` }}
          aria-hidden
        >
          {icon}
        </span>
      )}
      <span className="kpi-card__text">
        <span className="kpi-card__label">{label}</span>
        <span className="kpi-card__value" style={{ color: `var(--bp-${tone}-fg)` }}>
          {value}
        </span>
        {percent !== undefined && (
          <span
            className="kpi-card__progress"
            role="progressbar"
            aria-valuenow={Math.round(percent)}
            aria-valuemin={0}
            aria-valuemax={100}
          >
            <span
              style={{
                width: `${Math.max(0, Math.min(100, percent))}%`,
                background: `var(--bp-${tone}-solid, var(--bp-primary-solid))`,
              }}
            />
          </span>
        )}
        {sub && <span className="kpi-card__sub">{sub}</span>}
      </span>
    </>
  );
  return onClick ? (
    <button type="button" className="kpi-card kpi-card--clickable" onClick={onClick}>
      {content}
    </button>
  ) : (
    <div className="kpi-card kpi-card--static">{content}</div>
  );
}

/** Responsive strip of KPI cards (auto-fit, min 220px). */
export function KpiStrip({ children }: { children: ReactNode }) {
  return <div className="kpi-strip">{children}</div>;
}
