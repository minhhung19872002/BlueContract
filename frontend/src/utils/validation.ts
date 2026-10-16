import type { Rule } from 'antd/es/form';
import { z } from 'zod';

/** Mirrors backend PasswordPolicy: ≥ 8 chars with upper, lower, digit and special character. */
export const passwordSchema = z
  .string()
  .min(8, 'Mật khẩu phải có ít nhất 8 ký tự')
  .max(128)
  .regex(/[A-ZÀ-Ỹ]/, 'Mật khẩu phải có chữ hoa')
  .regex(/[a-zà-ỹ]/, 'Mật khẩu phải có chữ thường')
  .regex(/\d/, 'Mật khẩu phải có chữ số')
  .regex(/[^\p{L}\p{N}]/u, 'Mật khẩu phải có ký tự đặc biệt');

export const PASSWORD_RULE: Rule = {
  validator: (_, value: string | undefined) => {
    if (!value) return Promise.resolve();
    const result = passwordSchema.safeParse(value);
    return result.success ? Promise.resolve() : Promise.reject(new Error(result.error.issues[0]?.message));
  },
};

/** Vietnamese tax code: 10 digits or 10 digits + "-" + 3 digits. */
export const taxCodeSchema = z
  .string()
  .trim()
  .regex(/^\d{10}(-\d{3})?$/, 'Mã số thuế gồm 10 chữ số hoặc dạng 0123456789-001');

export const TAX_CODE_RULE: Rule = {
  validator: (_, value: string | undefined) => {
    if (!value) return Promise.resolve();
    const result = taxCodeSchema.safeParse(value);
    return result.success ? Promise.resolve() : Promise.reject(new Error(result.error.issues[0]?.message));
  },
};

export const USER_NAME_RULE: Rule = {
  pattern: /^[A-Za-z0-9._-]{3,64}$/,
  message: 'Tên đăng nhập 3–64 ký tự, chỉ gồm chữ không dấu, số, dấu chấm, gạch dưới, gạch ngang',
};
