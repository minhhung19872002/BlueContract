import { Alert, Empty, Modal, Segmented, Select, Spin } from 'antd';
import { ArrowRight } from 'lucide-react';
import { useState } from 'react';
import { fetchDocumentFile, useDocumentCompare, type DocumentVersionDto } from '../../api/documents';
import { toApiProblem } from '../../api/http';
import { PdfViewer } from '../../components/PdfViewer';
import { formatDateTime } from '../../utils/format';
import './compare.css';

type View = 'text' | 'pdf';

const versionOption = (v: DocumentVersionDto) => ({
  value: v.id,
  label: `v${v.versionNumber} — ${v.reason}${v.isCurrent ? ' (hiện tại)' : ''}`,
  title: `${v.createdByName ?? ''} · ${formatDateTime(v.createdAt)}`,
});

function VersionPdf({ documentId, version }: { documentId: string; version?: DocumentVersionDto }) {
  if (!version) return <Empty description="Chọn phiên bản" />;
  if (!version.hasPdf) return <Empty description={`v${version.versionNumber} chưa có bản PDF`} />;
  return (
    <PdfViewer
      load={() => fetchDocumentFile(documentId, version.id, 'pdf')}
      cacheKey={`${version.id}:${version.pdfSha256}`}
      height="64vh"
      fileName={`tai-lieu_v${version.versionNumber}.pdf`}
    />
  );
}

/**
 * "So sánh phiên bản" (spec §9.1-6): the text of two versions paragraph by paragraph (removed in red, added in green),
 * or both PDFs side by side.
 */
export function CompareVersionsDialog({
  documentId,
  versions,
  open,
  onClose,
}: {
  documentId: string;
  versions: DocumentVersionDto[];
  open: boolean;
  onClose: () => void;
}) {
  // Versions come newest first: compare the previous one with the current one by default.
  const [from, setFrom] = useState<string | undefined>(versions[1]?.id);
  const [to, setTo] = useState<string | undefined>(versions[0]?.id);
  const [view, setView] = useState<View>('text');
  const diff = useDocumentCompare(documentId, from, to);
  const fromVersion = versions.find((v) => v.id === from);
  const toVersion = versions.find((v) => v.id === to);
  const options = versions.map(versionOption);

  return (
    <Modal
      title="So sánh phiên bản"
      open={open}
      onCancel={onClose}
      footer={null}
      width="min(1280px, 96vw)"
      style={{ top: 16 }}
      destroyOnHidden
    >
      <div className="compare">
        <div className="compare__bar">
          <Select
            aria-label="Phiên bản cũ"
            className="compare__select"
            value={from}
            options={options}
            onChange={setFrom}
          />
          <ArrowRight size={16} aria-hidden className="compare__arrow" />
          <Select
            aria-label="Phiên bản mới"
            className="compare__select"
            value={to}
            options={options}
            onChange={setTo}
          />
          <span className="compare__spacer" />
          <Segmented<View>
            value={view}
            onChange={setView}
            options={[
              { value: 'text', label: 'Nội dung thay đổi' },
              { value: 'pdf', label: 'PDF song song' },
            ]}
          />
        </div>
        {versions.length < 2 ? (
          <Empty description="Tài liệu mới có một phiên bản" />
        ) : from === to ? (
          <Alert type="info" showIcon message="Chọn hai phiên bản khác nhau để so sánh." />
        ) : view === 'pdf' ? (
          <div className="compare__pdfs">
            <section>
              <div className="compare__pdf-title">Phiên bản cũ · v{fromVersion?.versionNumber}</div>
              <VersionPdf documentId={documentId} version={fromVersion} />
            </section>
            <section>
              <div className="compare__pdf-title">Phiên bản mới · v{toVersion?.versionNumber}</div>
              <VersionPdf documentId={documentId} version={toVersion} />
            </section>
          </div>
        ) : diff.isLoading ? (
          <Spin style={{ margin: 48 }} />
        ) : diff.isError ? (
          <Alert type="warning" showIcon message={toApiProblem(diff.error).title} />
        ) : diff.data ? (
          <>
            <div className="compare__summary" role="status">
              v{diff.data.fromVersion} → v{diff.data.toVersion}:{' '}
              <span className="compare__count compare__count--added">+{diff.data.added} đoạn thêm</span>
              <span className="compare__count compare__count--removed">−{diff.data.removed} đoạn bỏ</span>
              {diff.data.added === 0 && diff.data.removed === 0 && ' nội dung giống nhau'}
            </div>
            <ol className="compare__lines" aria-label="Nội dung so sánh">
              {diff.data.lines.map((l, i) => (
                <li key={i} className={`compare__line compare__line--${l.kind.toLowerCase()}`}>
                  <span className="compare__mark" aria-hidden>
                    {l.kind === 'Added' ? '+' : l.kind === 'Removed' ? '−' : ''}
                  </span>
                  <span className="sr-only">
                    {l.kind === 'Added' ? 'Thêm: ' : l.kind === 'Removed' ? 'Bỏ: ' : ''}
                  </span>
                  <span className="compare__text">{l.text}</span>
                </li>
              ))}
            </ol>
          </>
        ) : null}
      </div>
    </Modal>
  );
}
