import { Cloud, Smartphone, Usb } from 'lucide-react';
import { useState } from 'react';
import type { SigningProviderDto } from '../../api/signing';

const PROVIDER_KEY = 'bp-sign-provider';

export function providerIcon(p?: SigningProviderDto, size = 15) {
  return p?.mode === 'RemoteAsync' ? (
    <Smartphone size={size} strokeWidth={1.75} aria-hidden />
  ) : p?.mode === 'ClientSide' ? (
    <Usb size={size} strokeWidth={1.75} aria-hidden />
  ) : (
    <Cloud size={size} strokeWidth={1.75} aria-hidden />
  );
}

/** One line under the provider name in the pickers. */
export function providerHint(p: SigningProviderDto) {
  return p.mode === 'RemoteAsync'
    ? 'Ký số từ xa · xác nhận trên điện thoại'
    : p.mode === 'ClientSide'
      ? 'Token USB · cắm vào máy'
      : 'Ký trên máy chủ';
}

export function savedProviderCode(): string | undefined {
  try {
    return localStorage.getItem(PROVIDER_KEY) ?? undefined;
  } catch {
    return undefined;
  }
}

/**
 * Provider chosen by the user in "Trình ký" (remembered on this browser), falling back to the default of the system.
 * Used by the batch signing and as the initial choice of the sign modal.
 */
export function usePreferredProvider(list: SigningProviderDto[] | undefined) {
  const [code, setCode] = useState(savedProviderCode);
  const providers = list ?? [];
  const provider =
    providers.find((p) => p.code === code) ?? providers.find((p) => p.isDefault) ?? providers[0];
  const choose = (next: string) => {
    setCode(next);
    try {
      localStorage.setItem(PROVIDER_KEY, next);
    } catch {
      // Not remembered; the choice still applies on this page.
    }
  };
  return { provider, choose };
}
