/** Short money label for the axis: "1,2 tỷ", "350 tr". */
export function shortMoney(v: number) {
  if (v >= 1e9) return `${(v / 1e9).toLocaleString('vi-VN', { maximumFractionDigits: 1 })} tỷ`;
  if (v >= 1e6) return `${Math.round(v / 1e6).toLocaleString('vi-VN')} tr`;
  return v.toLocaleString('vi-VN');
}

/** Rounds the axis maximum up to 1, 2, 2.5 or 5 × 10ⁿ so the 4 grid lines get readable values. */
export function niceMax(v: number) {
  if (v <= 0) return 1;
  const pow = 10 ** Math.floor(Math.log10(v));
  const step = [1, 2, 2.5, 5, 10].find((m) => m * pow >= v) ?? 10;
  return step * pow;
}
