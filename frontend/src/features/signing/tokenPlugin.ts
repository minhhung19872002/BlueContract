import { isApiProblem } from '../../api/http';

/**
 * Bridge to the CA's local signing plugin for USB tokens (spec §8.2 ClientSideTokenProvider).
 *
 * Each CA ships its own plugin (VNPT-CA Plugin, Viettel-CA, FPT-CA...) listening on localhost. The hospital configures
 * the adapter's base URL with VITE_TOKEN_PLUGIN_URL; the adapter expects two JSON endpoints:
 *   GET  {base}/certificate          → { certificate: base64 DER }
 *   POST {base}/sign { data: base64 } → { signature: base64 RSA-SHA256 PKCS#1 v1.5 }
 * A thin shim for a specific CA plugin maps its native API onto these two calls (see docs/DEPLOY.md).
 * Without a plugin the signing dialog falls back to manual mode (paste certificate and signature value).
 */
const PLUGIN_URL = (import.meta.env.VITE_TOKEN_PLUGIN_URL as string | undefined)?.replace(/\/$/, '');

export const tokenPluginConfigured = !!PLUGIN_URL;

/** A problem with the local signing plugin, already worded for the user. */
export class TokenPluginError extends Error {}

async function call<T>(path: string, field: keyof T & string, init?: RequestInit): Promise<T[keyof T]> {
  if (!PLUGIN_URL) throw new TokenPluginError('Chưa cấu hình plugin ký số USB token.');
  const response = await fetch(`${PLUGIN_URL}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  });
  if (!response.ok)
    throw new TokenPluginError(
      response.status === 401 || response.status === 403
        ? 'Plugin ký số từ chối: chưa nhập đúng mã PIN của USB token hoặc người dùng đã hủy.'
        : `Plugin ký số báo lỗi (mã ${response.status}). Kiểm tra USB token đã cắm và phần mềm ký số đang chạy.`,
    );
  let body: T;
  try {
    body = (await response.json()) as T;
  } catch {
    throw new TokenPluginError('Plugin ký số trả về dữ liệu không đúng định dạng.');
  }
  const value = body?.[field];
  if (typeof value !== 'string' || value.length === 0)
    throw new TokenPluginError('Plugin ký số trả về dữ liệu không đúng định dạng.');
  return value;
}

export async function readTokenCertificate(): Promise<string> {
  return (await call<{ certificate: string }>('/certificate', 'certificate')) as string;
}

export async function signWithToken(dataBase64: string): Promise<string> {
  return (await call<{ signature: string }>('/sign', 'signature', {
    method: 'POST',
    body: JSON.stringify({ data: dataBase64 }),
  })) as string;
}

/**
 * A USB-token failure in Vietnamese: the plugin not running ("Failed to fetch"…), its own refusals, or the server's
 * message; never a raw browser / JSON error.
 */
export function describeTokenError(e: unknown, fallback = 'Không ký được bằng USB token.'): string {
  if (e instanceof TokenPluginError) return e.message;
  if (isApiProblem(e)) return e.title;
  const raw = e instanceof Error ? e.message : '';
  if (e instanceof TypeError || /failed to fetch|networkerror|load failed|network request failed/i.test(raw))
    return 'Không kết nối được plugin ký số trên máy. Hãy cắm USB token, mở phần mềm ký số của nhà cung cấp chứng thư rồi thử lại.';
  return fallback;
}

/** Accepts a .cer/.crt file in DER or PEM form and returns base64 DER. */
export async function certificateFileToBase64(file: File): Promise<string> {
  const bytes = new Uint8Array(await file.arrayBuffer());
  const text = new TextDecoder().decode(bytes);
  const pem = /-----BEGIN CERTIFICATE-----([\s\S]+?)-----END CERTIFICATE-----/.exec(text);
  if (pem?.[1]) return pem[1].replace(/\s+/g, '');
  let binary = '';
  bytes.forEach((b) => (binary += String.fromCharCode(b)));
  return btoa(binary);
}
