import { useMutation } from '@tanstack/react-query';
import { apiClient } from './http';
import { authStore } from '../auth/authStore';
import type { components } from './schema';

type S = components['schemas'];
export type LoginResult = S['LoginResult'];

function applyLogin(result: LoginResult) {
  if (result.accessToken && result.user) authStore.setSession(result.accessToken, result.user);
  return result;
}

export function useLogin() {
  return useMutation({
    mutationFn: async (body: S['LoginRequest']) =>
      applyLogin((await apiClient.post<LoginResult>('/api/auth/login', body)).data),
  });
}

export function useLoginTwoFactor() {
  return useMutation({
    mutationFn: async (body: S['TwoFactorLoginRequest']) =>
      applyLogin((await apiClient.post<LoginResult>('/api/auth/login/2fa', body)).data),
  });
}

export function useChangePassword() {
  return useMutation({
    mutationFn: async (body: S['ChangePasswordRequest']) => {
      await apiClient.post('/api/auth/change-password', body);
    },
  });
}

export function useTwoFactorSetup() {
  return useMutation({
    mutationFn: async () => (await apiClient.post<S['TwoFactorSetupResponse']>('/api/auth/2fa/setup')).data,
  });
}

export function useEnableTwoFactor() {
  return useMutation({
    mutationFn: async (body: S['EnableTwoFactorRequest']) => {
      await apiClient.post('/api/auth/2fa/enable', body);
    },
  });
}

export function useDisableTwoFactor() {
  return useMutation({
    mutationFn: async (body: S['DisableTwoFactorRequest']) => {
      await apiClient.post('/api/auth/2fa/disable', body);
    },
  });
}

export async function reloadCurrentUser() {
  const me = (await apiClient.get<S['CurrentUserDto']>('/api/auth/me')).data;
  authStore.setUser(me);
  return me;
}

export async function logoutRequest() {
  await apiClient.post('/api/auth/logout');
}

// ---------------- Signature images (spec §8) ----------------

export const SIGNATURE_IMAGE_URLS = {
  signature: '/api/account/signature-images/signature',
  initial: '/api/account/signature-images/initial',
  seal: '/api/admin/organization/seal',
} as const;

export type SignatureImageSlot = keyof typeof SIGNATURE_IMAGE_URLS;

/** The stored image, or null when none is set (404). */
export async function fetchSignatureImage(slot: SignatureImageSlot): Promise<Blob | null> {
  try {
    return (await apiClient.get<Blob>(SIGNATURE_IMAGE_URLS[slot], { responseType: 'blob' })).data;
  } catch (e) {
    if ((e as { response?: { status?: number } }).response?.status === 404) return null;
    throw e;
  }
}

export async function uploadSignatureImage(slot: SignatureImageSlot, file: File) {
  const data = new FormData();
  data.append('file', file);
  await apiClient.post(SIGNATURE_IMAGE_URLS[slot], data);
}

export async function removeSignatureImage(slot: SignatureImageSlot) {
  await apiClient.delete(SIGNATURE_IMAGE_URLS[slot]);
}

// ---------------- Organisation e-seal certificate (spec §8) ----------------

export type OrganizationCertificateDto = components['schemas']['OrganizationCertificateDto'];

const SEAL_CERTIFICATE = '/api/admin/organization/seal-certificate';

/** The installed certificate, or null when the e-seal still uses the test certificate (404). */
export async function fetchSealCertificate(): Promise<OrganizationCertificateDto | null> {
  try {
    return (await apiClient.get<OrganizationCertificateDto>(SEAL_CERTIFICATE)).data;
  } catch (e) {
    if ((e as { response?: { status?: number } }).response?.status === 404) return null;
    throw e;
  }
}

export async function installSealCertificate(file: File, password: string) {
  const data = new FormData();
  data.append('file', file);
  data.append('password', password);
  return (await apiClient.post<OrganizationCertificateDto>(SEAL_CERTIFICATE, data)).data;
}

export async function removeSealCertificate() {
  await apiClient.delete(SEAL_CERTIFICATE);
}
