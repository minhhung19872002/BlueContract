import { getSigningRequest, type SigningRequestDto } from '../../api/signing';

const POLL_INTERVAL_MS = 2_000;
const POLL_TIMEOUT_MS = 10 * 60_000;

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** Polls a remote-signing request until the user confirms on the phone (or it fails / expires). */
export async function waitForRemoteSignature(
  request: SigningRequestDto,
  isCancelled: () => boolean,
): Promise<SigningRequestDto> {
  const deadline = Date.now() + POLL_TIMEOUT_MS;
  let current = request;
  while (current.status === 'Pending' && Date.now() < deadline && !isCancelled()) {
    await sleep(POLL_INTERVAL_MS);
    current = await getSigningRequest(request.requestId);
  }
  return current;
}
