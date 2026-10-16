import { App, Upload } from 'antd';
import {
  ChevronDown,
  ChevronUp,
  FileCheck2,
  FileWarning,
  Lock,
  PenLine,
  ShieldCheck,
  ShieldQuestion,
  ShieldX,
  Upload as UploadIcon,
} from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { verifyUpload, type PdfSignatureInfo, type VerifyResultDto } from '../../api/signing';
import { toApiProblem } from '../../api/http';
import type { Tone } from '../../components/StatusTag';
import { PageHeader } from '../../components/common';
import { formatDateTime } from '../../utils/format';
import { verifyVerdict, type Verdict } from './verdict';
import './verify.css';

const VERDICTS: Record<Verdict, { tone: Tone; icon: typeof ShieldCheck; title: string }> = {
  valid: { tone: 'success', icon: ShieldCheck, title: 'Hợp lệ toàn bộ' },
  invalid: { tone: 'danger', icon: ShieldX, title: 'Có chữ ký không hợp lệ' },
  modified: { tone: 'orange', icon: FileWarning, title: 'Tài liệu bị sửa sau khi ký' },
  none: { tone: 'warning', icon: ShieldQuestion, title: 'Tệp không có chữ ký số' },
};

function verdictBody(verdict: Verdict, result: VerifyResultDto) {
  const total = result.signatureCount;
  const bad = result.signatures.filter((s) => !s.integrityValid).length;
  switch (verdict) {
    case 'valid':
      return `${total} chữ ký toàn vẹn, tài liệu không bị thay đổi sau khi ký.`;
    case 'invalid':
      return `${bad}/${total} chữ ký không hợp lệ: nội dung đã bị thay đổi sau khi ký hoặc chữ ký bị hỏng.`;
    case 'modified':
      return `Các chữ ký đều toàn vẹn nhưng tệp có thêm thay đổi sau chữ ký cuối cùng — phần thay đổi này không được ai ký.`;
    default:
      return 'Tệp PDF không chứa chữ ký số nào để kiểm tra.';
  }
}

/** Overall result banner of a check (success / danger / orange / warning). */
export function VerifyVerdictBanner({ result, meta }: { result: VerifyResultDto; meta?: ReactNode }) {
  const verdict = verifyVerdict(result);
  const v = VERDICTS[verdict];
  const Icon = v.icon;
  return (
    <div
      role="status"
      className="verify-verdict"
      style={{ borderColor: `var(--bp-${v.tone}-bd)`, background: `var(--bp-${v.tone}-bg)` }}
    >
      <span className="verify-verdict__icon" style={{ background: `var(--bp-${v.tone}-solid)` }} aria-hidden>
        <Icon size={22} strokeWidth={1.75} />
      </span>
      <div className="verify-verdict__text">
        <div className="verify-verdict__title" style={{ color: `var(--bp-${v.tone}-fg)` }}>
          {v.title}
        </div>
        <div className="verify-verdict__body">{verdictBody(verdict, result)}</div>
        {meta && <div className="verify-verdict__meta">{meta}</div>}
      </div>
    </div>
  );
}

/** "CN=Quản trị NCC3,E=…,O=…" → "Quản trị NCC3". */
function commonName(subject: string) {
  return /(?:^|,\s*)CN=([^,]+)/.exec(subject)?.[1] ?? subject;
}

function signatureState(s: PdfSignatureInfo, isLast: boolean): { tone: Tone; label: string } {
  if (!s.integrityValid) return { tone: 'danger', label: 'Không hợp lệ' };
  if (isLast && !s.coversWholeDocument) return { tone: 'orange', label: 'Có thay đổi sau chữ ký' };
  return { tone: 'success', label: 'Hợp lệ' };
}

/** Signatures of a PDF as an accordion: signer, time, status; details (CA, serial, timestamp, integrity) on open. */
export function SignatureList({ result }: { result: VerifyResultDto }) {
  const [open, setOpen] = useState<Record<number, boolean>>({ 0: true });
  const lastRevision = Math.max(0, ...result.signatures.map((s) => s.revision));
  return (
    <section className="verify-sigs">
      <div className="verify-sigs__head">Chữ ký trong tài liệu ({result.signatures.length})</div>
      {result.signatures.map((s, i) => {
        const state = signatureState(s, s.revision === lastRevision);
        const isOpen = !!open[i];
        const details: { k: string; v: ReactNode; tone?: Tone; mono?: boolean }[] = [
          { k: 'Người ký', v: s.signerName ?? s.signerSubject },
          { k: 'Chủ thể chứng thư', v: s.signerSubject },
          { k: 'Tổ chức chứng thực (CA)', v: s.issuerSubject },
          { k: 'Thời điểm ký', v: s.signingTime ? formatDateTime(s.signingTime) : '—' },
          { k: 'Dấu thời gian (timestamp)', v: s.hasTimestamp ? 'Có' : 'Không có' },
          {
            k: 'Toàn vẹn',
            v: s.integrityValid ? 'Toàn vẹn ✓' : 'Không toàn vẹn — nội dung thay đổi sau chữ ký',
            tone: s.integrityValid ? 'success' : 'danger',
          },
          {
            k: 'Phạm vi',
            v: s.coversWholeDocument
              ? 'Toàn bộ tài liệu'
              : `Bản sửa đổi ${s.revision}/${s.totalRevisions} (có phần ký tiếp theo)`,
          },
          { k: 'Serial chứng thư', v: s.serialNumber, mono: true },
          ...(s.isSelfSigned
            ? [
                {
                  k: 'Loại chứng thư',
                  v: 'Tự cấp (thử nghiệm) — không có giá trị pháp lý',
                  tone: 'orange' as Tone,
                },
              ]
            : []),
          ...(s.reason ? [{ k: 'Lý do ký', v: s.reason }] : []),
          ...(s.location ? [{ k: 'Nơi ký', v: s.location }] : []),
        ];
        return (
          <div key={s.fieldName} className="verify-sig">
            <button
              type="button"
              className="verify-sig__row"
              aria-expanded={isOpen}
              onClick={() => setOpen((o) => ({ ...o, [i]: !isOpen }))}
            >
              <span
                className="verify-sig__icon"
                style={{ background: `var(--bp-${state.tone}-bg)`, color: `var(--bp-${state.tone}-fg)` }}
                aria-hidden
              >
                <PenLine size={16} strokeWidth={1.75} />
              </span>
              <span className="verify-sig__who">
                <span className="verify-sig__name">
                  #{s.revision} · {s.signerName ?? s.signerSubject}
                </span>
                <span className="verify-sig__sub">
                  CA: {commonName(s.issuerSubject)}
                  {s.signingTime && ` · ký lúc ${formatDateTime(s.signingTime)}`}
                </span>
              </span>
              <span
                className="verify-sig__status"
                style={{
                  borderColor: `var(--bp-${state.tone}-bd)`,
                  background: `var(--bp-${state.tone}-bg)`,
                  color: `var(--bp-${state.tone}-fg)`,
                }}
              >
                <span style={{ background: `var(--bp-${state.tone}-solid)` }} aria-hidden />
                {state.label}
              </span>
              {isOpen ? <ChevronUp size={16} aria-hidden /> : <ChevronDown size={16} aria-hidden />}
            </button>
            {isOpen && (
              <dl className="verify-sig__details">
                {details.map((d) => (
                  <div key={d.k}>
                    <dt>{d.k}</dt>
                    <dd
                      className={d.mono ? 'mono' : undefined}
                      style={d.tone ? { color: `var(--bp-${d.tone}-fg)` } : undefined}
                    >
                      {d.v}
                    </dd>
                  </div>
                ))}
              </dl>
            )}
          </div>
        );
      })}
    </section>
  );
}

/** "1,2 MB" */
function fileSize(bytes: number) {
  if (bytes >= 1024 * 1024)
    return `${(bytes / 1024 / 1024).toLocaleString('vi-VN', { maximumFractionDigits: 1 })} MB`;
  return `${Math.max(1, Math.round(bytes / 1024)).toLocaleString('vi-VN')} KB`;
}

/** SHA-256 of the chosen file (only in a secure context; shortened as "4b8d10aa…03c9"). */
async function sha256(file: File): Promise<string | null> {
  if (!globalThis.crypto?.subtle) return null;
  const digest = await crypto.subtle.digest('SHA-256', await file.arrayBuffer());
  const hex = [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}…${hex.slice(-4)}`;
}

export function VerifySignaturePage() {
  const [result, setResult] = useState<{ name: string; meta: string; data: VerifyResultDto } | null>(null);
  const [loading, setLoading] = useState(false);
  const { message } = App.useApp();

  return (
    <div className="verify">
      <PageHeader
        title="Xác minh chữ ký số"
        subtitle="Tải lên tệp PDF để kiểm tra các chữ ký số và tính toàn vẹn"
      />
      <Upload.Dragger
        className={`verify-drop${result ? ' verify-drop--compact' : ''}`}
        accept=".pdf,application/pdf"
        showUploadList={false}
        disabled={loading}
        beforeUpload={(file) => {
          setLoading(true);
          Promise.all([verifyUpload(file), sha256(file).catch(() => null)])
            .then(([data, hash]) =>
              setResult({
                name: file.name,
                meta: [file.name, fileSize(file.size), hash && `SHA-256 ${hash}`].filter(Boolean).join(' · '),
                data,
              }),
            )
            .catch((e) => message.error(toApiProblem(e).title))
            .finally(() => setLoading(false));
          return false;
        }}
      >
        <span className="verify-drop__inner">
          <span className="verify-drop__icon" aria-hidden>
            {result ? (
              <FileCheck2 size={24} strokeWidth={1.75} />
            ) : (
              <UploadIcon size={24} strokeWidth={1.75} />
            )}
          </span>
          <span className="verify-drop__text">
            <span className="verify-drop__title">
              {loading
                ? 'Đang xác minh…'
                : result
                  ? result.name
                  : 'Kéo thả tệp PDF vào đây hoặc bấm để chọn tệp'}
            </span>
            <span className="verify-drop__sub">{result ? 'Bấm để kiểm tra tệp khác' : 'Chỉ nhận PDF'}</span>
          </span>
        </span>
      </Upload.Dragger>
      <div className="verify-note">
        <Lock size={14} aria-hidden />
        Tệp chỉ dùng để kiểm tra, không được lưu lại trên hệ thống.
      </div>
      {result && (
        <>
          <VerifyVerdictBanner result={result.data} meta={result.meta} />
          {result.data.signatures.length > 0 && <SignatureList result={result.data} />}
        </>
      )}
    </div>
  );
}
