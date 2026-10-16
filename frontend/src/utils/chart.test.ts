import { describe, expect, it } from 'vitest';
import { niceMax, shortMoney } from './chart';

describe('chart axis helpers', () => {
  it('rounds the axis maximum up to 1, 2, 2.5 or 5 × 10ⁿ', () => {
    expect(niceMax(0)).toBe(1);
    expect(niceMax(870_000_000)).toBe(1_000_000_000);
    expect(niceMax(1_300_000_000)).toBe(2_000_000_000);
    expect(niceMax(2_100_000_000)).toBe(2_500_000_000);
    expect(niceMax(4_000_000)).toBe(5_000_000);
    expect(niceMax(5_000_000)).toBe(5_000_000);
  });

  it('labels money in tỷ / triệu', () => {
    expect(shortMoney(1_250_000_000)).toBe('1,3 tỷ');
    expect(shortMoney(350_000_000)).toBe('350 tr');
    expect(shortMoney(0)).toBe('0');
  });
});
