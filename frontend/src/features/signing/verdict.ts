import type { VerifyResultDto } from '../../api/signing';

export type Verdict = 'none' | 'valid' | 'invalid' | 'modified';

/**
 * Overall result of a PDF check (design/Xac minh.dc.html):
 * - invalid: at least one signature fails its integrity check;
 * - modified: every signature is intact but bytes were appended after the last one (changes nobody signed);
 * - valid: every signature intact and the last one covers the whole file.
 */
export function verifyVerdict(result: VerifyResultDto): Verdict {
  if (result.signatureCount === 0 || result.signatures.length === 0) return 'none';
  if (result.signatures.some((s) => !s.integrityValid)) return 'invalid';
  const last = result.signatures.reduce((a, b) => (b.revision > a.revision ? b : a));
  return last.coversWholeDocument ? 'valid' : 'modified';
}
