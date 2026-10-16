import { passwordChecks, PASSWORD_STRENGTH, passwordSchema, taxCodeSchema } from './validation';
import { omitKey } from './format';
import { statusLabel, statusOptions } from '../components/StatusTag';

describe('passwordSchema (mirrors backend PasswordPolicy)', () => {
  it.each([
    ['Demo@123', true],
    ['demo@123', false],
    ['DEMO@123', false],
    ['Demo1234', false],
    ['Demo@abc', false],
    ['De@1', false],
  ])('%s → %s', (value, ok) => {
    expect(passwordSchema.safeParse(value).success).toBe(ok);
  });
});

describe('taxCodeSchema', () => {
  it('accepts 10 digits and branch format', () => {
    expect(taxCodeSchema.safeParse('0101234567').success).toBe(true);
    expect(taxCodeSchema.safeParse('0101234567-001').success).toBe(true);
  });
  it('rejects other shapes', () => {
    expect(taxCodeSchema.safeParse('12345').success).toBe(false);
    expect(taxCodeSchema.safeParse('0101234567-1').success).toBe(false);
  });
});

describe('status labels', () => {
  it('maps values to Vietnamese labels in one place', () => {
    expect(statusLabel('userStatus', 'Active')).toBe('Hoạt động');
    expect(statusLabel('active', false)).toBe('Ngừng hoạt động');
    expect(statusLabel('departmentType', 'Unknown')).toBe('Unknown');
    expect(statusOptions('userType').map((o) => o.value)).toEqual(['Internal', 'Supplier']);
  });
});

describe('omitKey', () => {
  it('removes a key without mutating the input', () => {
    const input = { a: 1, b: 2 };
    expect(omitKey(input, 'a')).toEqual({ b: 2 });
    expect(input).toEqual({ a: 1, b: 2 });
  });
});

describe('password checklist', () => {
  it('ticks each rule and agrees with the schema', () => {
    const weak = passwordChecks('abc');
    expect(weak.filter((c) => c.ok).map((c) => c.label)).toEqual(['Có chữ thường']);
    const strong = passwordChecks('Bvdk@2026');
    expect(strong.every((c) => c.ok)).toBe(true);
    expect(passwordSchema.safeParse('Bvdk@2026').success).toBe(true);
    expect(PASSWORD_STRENGTH[strong.filter((c) => c.ok).length]).toBe('Mạnh');
  });
});
