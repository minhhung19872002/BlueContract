import { App, Form, Input } from 'antd';
import type { ReactNode } from 'react';
import type { Tone } from './StatusTag';

type ModalApi = ReturnType<typeof App.useApp>['modal'];

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

/**
 * Confirmation of a destructive action (hủy, thu hồi, trả lại…) with a mandatory "Lý do" (design pattern "Modal xác nhận
 * nguy hiểm"). Resolves with the reason, or never when cancelled.
 */
export function confirmWithReason(
  modal: ModalApi,
  {
    title,
    content,
    okText = 'Xác nhận',
    reasonLabel = 'Lý do',
    onConfirm,
  }: {
    title: ReactNode;
    content?: ReactNode;
    okText?: string;
    reasonLabel?: string;
    onConfirm: (reason: string) => Promise<unknown> | void;
  },
) {
  let reason = '';
  const instance = modal.confirm({
    title,
    okText,
    okButtonProps: { danger: true, disabled: true },
    cancelText: 'Hủy',
    content: (
      <Form layout="vertical" style={{ marginTop: 12 }}>
        {content && <div style={{ marginBottom: 12 }}>{content}</div>}
        <Form.Item label={reasonLabel} required style={{ marginBottom: 0 }}>
          <Input.TextArea
            autoFocus
            rows={3}
            maxLength={1000}
            showCount
            onChange={(e) => {
              reason = e.target.value;
              instance.update({ okButtonProps: { danger: true, disabled: !reason.trim() } });
            }}
          />
        </Form.Item>
      </Form>
    ),
    onOk: () => onConfirm(reason.trim()),
  });
  return instance;
}
