import { describeTokenError, TokenPluginError } from './tokenPlugin';

describe('describeTokenError', () => {
  it('explains that the local plugin is not running instead of "Failed to fetch"', () => {
    expect(describeTokenError(new TypeError('Failed to fetch'))).toMatch(/Không kết nối được plugin ký số/);
    expect(describeTokenError(new Error('NetworkError when attempting to fetch resource.'))).toMatch(
      /Không kết nối được plugin ký số/,
    );
  });

  it('keeps messages already written for the user and the API titles', () => {
    expect(describeTokenError(new TokenPluginError('Plugin ký số báo lỗi (mã 500).'))).toBe(
      'Plugin ký số báo lỗi (mã 500).',
    );
    expect(describeTokenError({ status: 400, code: 'X', title: 'Chứng thư hết hạn.' })).toBe(
      'Chứng thư hết hạn.',
    );
  });

  it('never shows a raw technical message', () => {
    expect(describeTokenError(new SyntaxError('Unexpected token < in JSON at position 0'))).toBe(
      'Không ký được bằng USB token.',
    );
    expect(describeTokenError(undefined, 'Ký không thành công')).toBe('Ký không thành công');
  });
});
