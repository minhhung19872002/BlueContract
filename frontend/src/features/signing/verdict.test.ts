import { describe, expect, it } from 'vitest';
import type { PdfSignatureInfo, VerifyResultDto } from '../../api/signing';
import { verifyVerdict } from './verdict';

const sig = (revision: number, total: number, patch: Partial<PdfSignatureInfo> = {}): PdfSignatureInfo => ({
  fieldName: `Sig${revision}`,
  revision,
  totalRevisions: total,
  coversWholeDocument: revision === total,
  integrityValid: true,
  signerSubject: 'CN=A',
  signerName: 'A',
  issuerSubject: 'CN=CA',
  serialNumber: '01',
  isSelfSigned: false,
  signingTime: null,
  hasTimestamp: false,
  reason: null,
  location: null,
  ...patch,
});

const result = (signatures: PdfSignatureInfo[]): VerifyResultDto => ({
  signatureCount: signatures.length,
  allValid: signatures.every((s) => s.integrityValid),
  signatures,
});

describe('verifyVerdict', () => {
  it('is valid when every signature is intact and the last one covers the file', () => {
    expect(verifyVerdict(result([sig(1, 2), sig(2, 2)]))).toBe('valid');
  });

  it('is invalid as soon as one signature fails its integrity check', () => {
    expect(verifyVerdict(result([sig(1, 2), sig(2, 2, { integrityValid: false })]))).toBe('invalid');
  });

  it('flags changes appended after the last signature', () => {
    expect(verifyVerdict(result([sig(1, 3), sig(2, 3)]))).toBe('modified');
  });

  it('reports a file without signatures', () => {
    expect(verifyVerdict(result([]))).toBe('none');
  });
});
