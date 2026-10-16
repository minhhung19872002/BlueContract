import { Space, Tooltip, Typography } from 'antd';
import { formatMoney } from '../utils/format';

export interface BarSeries {
  key: string;
  label: string;
  color: string;
}

/**
 * Small dependency-free grouped bar chart (SVG) for the dashboards: one group per category (e.g. month), one bar per series.
 * Values are money; the tooltip shows "1.234.567 ₫".
 */
export function BarChart<T extends Record<string, number | string>>({
  data,
  category,
  series,
  height = 220,
}: {
  data: T[];
  category: keyof T & string;
  series: BarSeries[];
  height?: number;
}) {
  const max = Math.max(1, ...data.flatMap((d) => series.map((s) => Number(d[s.key]) || 0)));
  const groupWidth = 100 / Math.max(1, data.length);
  const barWidth = (groupWidth * 0.7) / series.length;
  const chartHeight = height - 24;
  const short = (v: number) =>
    v >= 1e9
      ? `${(v / 1e9).toFixed(1)} tỷ`
      : v >= 1e6
        ? `${(v / 1e6).toFixed(0)} tr`
        : v.toLocaleString('vi-VN');

  return (
    <div>
      <Space size="large" style={{ marginBottom: 8 }}>
        {series.map((s) => (
          <Space key={s.key} size={4}>
            <span
              style={{ display: 'inline-block', width: 12, height: 12, background: s.color, borderRadius: 2 }}
            />
            <Typography.Text type="secondary">{s.label}</Typography.Text>
          </Space>
        ))}
        <Typography.Text type="secondary">Cao nhất: {short(max)}</Typography.Text>
      </Space>
      <svg
        width="100%"
        height={height}
        viewBox={`0 0 100 ${height}`}
        preserveAspectRatio="none"
        role="img"
        aria-label="Biểu đồ"
      >
        <line x1="0" x2="100" y1={chartHeight} y2={chartHeight} stroke="#d9d9d9" strokeWidth="0.3" />
        {data.map((d, i) =>
          series.map((s, j) => {
            const value = Number(d[s.key]) || 0;
            const h = (value / max) * (chartHeight - 4);
            const x = i * groupWidth + groupWidth * 0.15 + j * barWidth;
            return (
              <Tooltip
                key={`${i}-${s.key}`}
                title={`${String(d[category])} — ${s.label}: ${formatMoney(value)}`}
              >
                <rect
                  x={x}
                  y={chartHeight - h}
                  width={barWidth * 0.9}
                  height={Math.max(h, value > 0 ? 0.5 : 0)}
                  fill={s.color}
                  rx="0.3"
                />
              </Tooltip>
            );
          }),
        )}
      </svg>
      <div style={{ display: 'flex', marginTop: -20 }}>
        {data.map((d) => (
          <div
            key={String(d[category])}
            style={{ width: `${groupWidth}%`, textAlign: 'center', fontSize: 11, color: '#8c8c8c' }}
          >
            {String(d[category])}
          </div>
        ))}
      </div>
    </div>
  );
}
