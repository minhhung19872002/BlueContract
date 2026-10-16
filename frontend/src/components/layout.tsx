import { ArrowLeft, CircleX, Info, TriangleAlert } from 'lucide-react';
import type { ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import type { Tone } from './StatusTag';
import './layout.css';

/**
 * Header of a document page (design/Page Detail.dc.html): back link, number in mono 20/600 with tags, title, sub line,
 * actions on the right.
 */
export function DocHeader({
  code,
  tags,
  title,
  sub,
  actions,
  back,
}: {
  code: ReactNode;
  tags?: ReactNode;
  title?: ReactNode;
  sub?: ReactNode;
  actions?: ReactNode;
  /** Path (or -1 for history back) and label of the back link. */
  back?: { to: string | -1; label: string };
}) {
  const navigate = useNavigate();
  return (
    <div className="doc-header">
      {back && (
        <button
          type="button"
          className="doc-header__back"
          onClick={() => (back.to === -1 ? navigate(-1) : navigate(back.to))}
        >
          <ArrowLeft size={15} aria-hidden />
          {back.label}
        </button>
      )}
      <div className="doc-header__row">
        <div className="doc-header__text">
          <div className="doc-header__code-row">
            <h1 className="doc-header__code">{code}</h1>
            {tags}
          </div>
          {title && <div className="doc-header__title">{title}</div>}
          {sub && <div className="doc-header__sub">{sub}</div>}
        </div>
        {actions && <div className="doc-header__actions">{actions}</div>}
      </div>
    </div>
  );
}

/** One figure of the KPI band of a document, with an optional progress bar and hint. */
export interface DocKpi {
  label: ReactNode;
  value: ReactNode;
  percent?: number;
  tone?: Tone;
  hint?: ReactNode;
}

export function DocKpis({ items }: { items: DocKpi[] }) {
  return (
    <div className="doc-kpis">
      {items.map((k, i) => (
        <div key={i} className="doc-kpis__item">
          <span className="doc-kpis__label">{k.label}</span>
          <span className="doc-kpis__value">{k.value}</span>
          {k.percent !== undefined && (
            <span
              className="doc-kpis__bar"
              role="progressbar"
              aria-valuenow={Math.round(k.percent)}
              aria-valuemin={0}
              aria-valuemax={100}
            >
              <span
                style={{
                  width: `${Math.max(0, Math.min(100, k.percent))}%`,
                  background: `var(--bp-${k.tone ?? 'primary'}-solid, var(--bp-primary-solid))`,
                }}
              />
            </span>
          )}
          {k.hint && <span className="doc-kpis__hint">{k.hint}</span>}
        </div>
      ))}
    </div>
  );
}

/** Check banner (validation results, warnings) with a title and a list. */
export function Banner({
  tone,
  title,
  items,
  children,
}: {
  tone: 'danger' | 'warning' | 'info' | 'success';
  title: ReactNode;
  items?: ReactNode[];
  children?: ReactNode;
}) {
  const Icon = tone === 'danger' ? CircleX : tone === 'warning' ? TriangleAlert : Info;
  return (
    <div
      role={tone === 'danger' || tone === 'warning' ? 'alert' : 'status'}
      className="doc-banner"
      style={{
        borderColor: `var(--bp-${tone}-bd)`,
        background: `var(--bp-${tone}-bg)`,
        color: `var(--bp-${tone}-fg)`,
      }}
    >
      <Icon size={20} strokeWidth={1.75} aria-hidden className="doc-banner__icon" />
      <div className="doc-banner__body">
        <div className="doc-banner__title">{title}</div>
        {items && items.length > 0 && (
          <ul className="doc-banner__list">
            {items.map((item, i) => (
              <li key={i}>{item}</li>
            ))}
          </ul>
        )}
        {children}
      </div>
    </div>
  );
}

/** Two-column document layout: content + tabs on the left, a 360px sticky column on the right (one column under 1180px). */
export function DetailGrid({ main, side }: { main: ReactNode; side?: ReactNode }) {
  return (
    <div className={`detail-grid${side ? ' detail-grid--with-side' : ''}`}>
      <div className="detail-grid__main">{main}</div>
      {side && <aside className="detail-grid__side">{side}</aside>}
    </div>
  );
}

/** Description list in a card (design "dl" grid: caption label above the value). */
export function InfoGrid({
  items,
  minColumn = 200,
}: {
  items: { label: ReactNode; value: ReactNode; mono?: boolean; span?: 'full' }[];
  minColumn?: number;
}) {
  return (
    <dl
      className="info-grid"
      style={{ gridTemplateColumns: `repeat(auto-fit, minmax(${minColumn}px, 1fr))` }}
    >
      {items.map((it, i) => (
        <div key={i} className={`info-grid__item${it.span === 'full' ? ' info-grid__item--full' : ''}`}>
          <dt>{it.label}</dt>
          <dd className={it.mono ? 'mono' : undefined}>
            {it.value === null || it.value === undefined || it.value === '' ? '—' : it.value}
          </dd>
        </div>
      ))}
    </dl>
  );
}

/** Form section with a title (design/Form Sections.dc.html): label-above inputs on a 2-column grid. */
export function FormSection({
  title,
  description,
  extra,
  children,
}: {
  title: ReactNode;
  description?: ReactNode;
  extra?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="form-section">
      <div className="form-section__head">
        <div>
          <div className="form-section__title">{title}</div>
          {description && <div className="form-section__desc">{description}</div>}
        </div>
        {extra}
      </div>
      <div className="form-section__body">{children}</div>
    </section>
  );
}

/** Action bar stuck to the bottom of a long form; the total (or a status) on the left, buttons on the right. */
export function StickyActionBar({ summary, children }: { summary?: ReactNode; children: ReactNode }) {
  return (
    <div className="sticky-actions">
      <div className="sticky-actions__summary">{summary}</div>
      <div className="sticky-actions__buttons">{children}</div>
    </div>
  );
}
