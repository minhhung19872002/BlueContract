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

async function call<T>(path: string, init?: RequestInit): Promise<T> {
  if (!PLUGIN_URL) throw new Error('Chưa cấu hình plugin ký số USB token.');
  const response = await fetch(`${PLUGIN_URL}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  });
  if (!response.ok) throw new Error(`Plugin ký số trả về lỗi ${response.status}.`);
  return (await response.json()) as T;
}

export async function readTokenCertificate(): Promise<string> {
  return (await call<{ certificate: string }>('/certificate')).certificate;
}

export async function signWithToken(dataBase64: string): Promise<string> {
  return (
    await call<{ signature: string }>('/sign', { method: 'POST', body: JSON.stringify({ data: dataBase64 }) })
  ).signature;
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
