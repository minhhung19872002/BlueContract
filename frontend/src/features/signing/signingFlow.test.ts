import { afterEach, describe, expect, it, vi } from 'vitest';
import type { SigningRequestDto } from '../../api/signing';
import { portalLink } from '../../realtime/useRealtime';
import { waitForRemoteSignature } from './signingFlow';

const getSigningRequest = vi.fn<(id: string) => Promise<SigningRequestDto>>();
vi.mock('../../api/signing', () => ({ getSigningRequest: (id: string) => getSigningRequest(id) }));

const request = (status: SigningRequestDto['status']): SigningRequestDto => ({
  requestId: 'r1',
  stepId: 's1',
  documentId: 'd1',
  provider: 'MOCK_REMOTE',
  mode: 'RemoteAsync',
  status,
  dataToSign: null,
  message: null,
  error: null,
  signatureId: status === 'Completed' ? 'sig' : null,
});

describe('waitForRemoteSignature', () => {
  afterEach(() => {
    vi.useRealTimers();
    getSigningRequest.mockReset();
  });

  it('polls until the user confirms on the phone', async () => {
    vi.useFakeTimers();
    getSigningRequest.mockResolvedValueOnce(request('Pending')).mockResolvedValueOnce(request('Completed'));

    const done = waitForRemoteSignature(request('Pending'), () => false);
    await vi.advanceTimersByTimeAsync(4_000);

    await expect(done).resolves.toMatchObject({ status: 'Completed', signatureId: 'sig' });
    expect(getSigningRequest).toHaveBeenCalledTimes(2);
  });

  it('stops polling when cancelled', async () => {
    vi.useFakeTimers();
    let cancelled = false;
    getSigningRequest.mockResolvedValue(request('Pending'));

    const done = waitForRemoteSignature(request('Pending'), () => cancelled);
    await vi.advanceTimersByTimeAsync(2_000);
    cancelled = true;
    await vi.advanceTimersByTimeAsync(2_000);

    await expect(done).resolves.toMatchObject({ status: 'Pending' });
    expect(getSigningRequest).toHaveBeenCalledTimes(2);
  });

  it('returns immediately for a request that is already final', async () => {
    await expect(waitForRemoteSignature(request('Failed'), () => false)).resolves.toMatchObject({
      status: 'Failed',
    });
    expect(getSigningRequest).not.toHaveBeenCalled();
  });
});

describe('portalLink', () => {
  it('prefixes internal links for supplier users only', () => {
    expect(portalLink('/trinh-ky?document=1', true)).toBe('/ncc/trinh-ky?document=1');
    expect(portalLink('/trinh-ky?document=1', false)).toBe('/trinh-ky?document=1');
    expect(portalLink('/ncc/trinh-ky', true)).toBe('/ncc/trinh-ky');
    expect(portalLink(null, true)).toBeNull();
  });
});
