import { Tooltip } from 'antd';
import { formatMoney } from '../utils/format';
import { niceMax, shortMoney } from '../utils/chart';
import './barChart.css';

export interface BarSeries {
  key: string;
  label: string;
  /** CSS colour; design tokens such as `var(--bp-success-solid)` work in both themes. */
  color: string;
}

/**
 * Dependency-free bar chart for the dashboards (design/Tong quan.dc.html): one column per category (e.g. month),
 * series side by side or stacked, value axis with 4 grid lines, tooltip listing every series and the total.
 * Values are money ("1.234.567 ₫").
 */
export function BarChart<T extends Record<string, number | string>>({
  data,
  category,
  series,
  height = 220,
  stacked = false,
  label = 'Biểu đồ',
  emptyText = 'Chưa có số liệu trong khoảng thời gian này',
}: {
  data: T[];
  category: keyof T & string;
  series: BarSeries[];
  height?: number;
  stacked?: boolean;
  label?: string;
  emptyText?: string;
}) {
  const value = (d: T, key: string) => Number(d[key]) || 0;
  const total = (d: T) => series.reduce((sum, s) => sum + value(d, s.key), 0);
  const max = niceMax(
    Math.max(0, ...data.map((d) => (stacked ? total(d) : Math.max(...series.map((s) => value(d, s.key)))))),
  );
  const ticks = [1, 0.75, 0.5, 0.25, 0];
  const empty = data.every((d) => series.every((x) => value(d, x.key) === 0));

  return (
    <figure className="bar-chart" aria-label={label}>
      <div className="bar-chart__legend">
        {series.map((s) => (
          <span key={s.key} className="bar-chart__legend-item">
            <span className="bar-chart__swatch" style={{ background: s.color }} aria-hidden />
            {s.label}
          </span>
        ))}
      </div>
      {empty && (
        <div className="bar-chart__empty" style={{ height }}>
          {emptyText}
        </div>
      )}
      <div className="bar-chart__plot" style={{ height }} hidden={empty}>
        <div className="bar-chart__axis" aria-hidden>
          {ticks.map((t) => (
            <span key={t}>{shortMoney(max * t)}</span>
          ))}
        </div>
        <div className="bar-chart__area">
          <div className="bar-chart__grid" aria-hidden>
            {ticks.map((t) => (
              <span key={t} />
            ))}
          </div>
          <div className="bar-chart__columns">
            {data.map((d) => {
              const name = String(d[category]);
              const tip = (
                <div className="bar-chart__tip">
                  <div className="bar-chart__tip-title">{name}</div>
                  {series.map((s) => (
                    <div key={s.key} className="bar-chart__tip-row">
                      <span className="bar-chart__swatch" style={{ background: s.color }} aria-hidden />
                      <span>{s.label}</span>
                      <span className="bar-chart__tip-value">{formatMoney(value(d, s.key))}</span>
                    </div>
                  ))}
                  {series.length > 1 && (
                    <div className="bar-chart__tip-row bar-chart__tip-total">
                      <span />
                      <span>Tổng</span>
                      <span className="bar-chart__tip-value">{formatMoney(total(d))}</span>
                    </div>
                  )}
                </div>
              );
              return (
                <Tooltip
                  key={name}
                  title={tip}
                  color="var(--bp-card)"
                  overlayInnerStyle={{ color: 'var(--bp-text)' }}
                >
                  <div
                    className={`bar-chart__column${stacked ? ' bar-chart__column--stacked' : ''}`}
                    tabIndex={0}
                    aria-label={`${name}: ${series.map((s) => `${s.label} ${formatMoney(value(d, s.key))}`).join(', ')}`}
                  >
                    {(stacked ? [...series].reverse() : series).map((s) => {
                      const v = value(d, s.key);
                      return (
                        <span
                          key={s.key}
                          className="bar-chart__bar"
                          style={{
                            background: s.color,
                            height: `${(v / max) * 100}%`,
                            minHeight: v > 0 ? 2 : 0,
                          }}
                        />
                      );
                    })}
                  </div>
                </Tooltip>
              );
            })}
          </div>
        </div>
      </div>
      <div className="bar-chart__labels" aria-hidden hidden={empty}>
        {data.map((d) => (
          <span key={String(d[category])}>{String(d[category])}</span>
        ))}
      </div>
    </figure>
  );
}
