import { App, Button, Drawer, Empty, Input, Spin, Switch, Tooltip } from 'antd';
import {
  ClipboardCheck,
  Copy,
  Download,
  ExternalLink,
  Eye,
  FileCheck2,
  FileText,
  FileX2,
  FolderDown,
  Merge,
  Search,
  ShieldCheck,
  Wallet,
  Warehouse,
  type LucideIcon,
} from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { usePagedList } from '../../api/admin';
import { downloadFile } from '../../api/http';
import {
  ARCHIVE,
  archiveApi,
  useArchiveComplete,
  useArchiveTree,
  type ArchiveFileDto,
  type ArchiveNodeDto,
  type ArchiveSearchItemDto,
} from '../../api/reports';
import { useAuth } from '../../auth/useAuth';
import { P } from '../../auth/permissions';
import { StatusTag, type Tone } from '../../components/StatusTag';
import { PageHeader, useApiError } from '../../components/common';
import { Banner } from '../../components/layout';
import { formatDate, formatDateTime } from '../../utils/format';
import { DocumentPdf } from '../proposals/ProposalDetailPage';
import './archive.css';

/** Icon and colour of each stage of the dossier (folder keys from the API). */
const STAGES: Record<string, { icon: LucideIcon; tone: Tone }> = {
  'folder-bid': { icon: FileCheck2, tone: 'primary' },
  'folder-contract': { icon: FileText, tone: 'primary' },
  'folder-proposal': { icon: Merge, tone: 'info' },
  'folder-receipt': { icon: Warehouse, tone: 'orange' },
  'folder-acceptance': { icon: ClipboardCheck, tone: 'success' },
  'folder-valuation': { icon: Wallet, tone: 'success' },
  'folder-liquidation': { icon: FileX2, tone: 'info' },
};

const shortSha = (sha: string) => `${sha.slice(0, 8)}…${sha.slice(-4)}`;

/** Hồ sơ lưu trữ (spec §9.1-12, design/Ho so luu tru.dc.html): dossiers on the left, stages of the selected one on the right. */
export function ArchivePage() {
  const [params, setParams] = useSearchParams();
  const [search, setSearch] = useState(params.get('q') ?? '');
  const contractId = params.get('contract') ?? undefined;
  const results = usePagedList<ArchiveSearchItemDto>(ARCHIVE, {
    search: params.get('q') ?? undefined,
    pageSize: 50,
  });
  const items = results.data?.items ?? [];

  return (
    <>
      <PageHeader
        title="Hồ sơ lưu trữ"
        subtitle="Hồ sơ điện tử theo hợp đồng: từ quyết định thầu đến thanh lý, tải trọn bộ kèm mục lục và mã SHA-256"
      />
      <div className="archive">
        <section className="archive-list">
          <div className="archive-list__search">
            <Input
              allowClear
              prefix={<Search size={16} aria-hidden />}
              placeholder="Số hợp đồng, số quyết định, tên NCC"
              aria-label="Tìm hồ sơ"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                if (!e.target.value) setParams((p) => (p.delete('q'), p));
              }}
              onPressEnter={() =>
                setParams((p) => (search.trim() ? p.set('q', search.trim()) : p.delete('q'), p))
              }
            />
          </div>
          <div className="archive-list__items" role="listbox" aria-label="Hồ sơ hợp đồng">
            {results.isFetching && items.length === 0 && <Spin style={{ margin: 24 }} />}
            {!results.isFetching && items.length === 0 && (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="Không tìm thấy hồ sơ"
                style={{ margin: 24 }}
              />
            )}
            {items.map((r) => {
              const on = r.contractId === contractId;
              return (
                <button
                  key={r.contractId}
                  type="button"
                  role="option"
                  aria-selected={on}
                  className={`archive-list__item${on ? ' is-active' : ''}`}
                  onClick={() => setParams((p) => (p.set('contract', r.contractId), p))}
                >
                  <span className="archive-list__row">
                    <span className="archive-list__number mono">{r.contractNumber}</span>
                    <StatusTag domain="contractStatus" value={r.status} />
                  </span>
                  <span className="archive-list__supplier">{r.supplierName}</span>
                  <span className="archive-list__meta">
                    {r.documentCount} tài liệu · {r.contractName}
                    {r.isArchiveComplete && ' · đã đầy đủ'}
                  </span>
                </button>
              );
            })}
          </div>
        </section>
        {contractId ? (
          <Dossier contractId={contractId} />
        ) : (
          <section className="archive-dossier archive-dossier--empty">
            <Empty description="Chọn một hồ sơ hợp đồng" />
          </section>
        )}
      </div>
    </>
  );
}

function FileRow({ file, contractId }: { file: ArchiveFileDto; contractId: string }) {
  const { message } = App.useApp();
  const onError = useApiError();
  const open = () =>
    archiveApi.fileLink(contractId, file.id).then((l) => window.open(l.url, '_blank', 'noopener'), onError);
  return (
    <div className="archive-file">
      <span className="archive-file__kind">{file.kind}</span>
      {file.sha256 && (
        <span className="archive-file__sha mono" data-sha={file.sha256} title={`SHA-256 ${file.sha256}`}>
          {shortSha(file.sha256)}
          <Tooltip title="Sao chép SHA-256">
            <button
              type="button"
              className="archive-icon-btn archive-icon-btn--ghost"
              aria-label="Sao chép SHA-256"
              onClick={() =>
                navigator.clipboard
                  ?.writeText(file.sha256!)
                  .then(() => message.success('Đã sao chép SHA-256'))
                  .catch(() => message.info(file.sha256!))
              }
            >
              <Copy size={13} aria-hidden />
            </button>
          </Tooltip>
        </span>
      )}
      <Tooltip title={`Tải ${file.fileName}`}>
        <button type="button" className="archive-icon-btn" aria-label={`Tải ${file.fileName}`} onClick={open}>
          <Download size={15} aria-hidden />
        </button>
      </Tooltip>
    </div>
  );
}

function DocCard({
  node,
  contractId,
  onView,
}: {
  node: ArchiveNodeDto;
  contractId: string;
  onView: (node: ArchiveNodeDto) => void;
}) {
  return (
    <div className="archive-doc">
      <div className="archive-doc__head">
        <FileText size={16} aria-hidden className="archive-doc__icon" />
        <span className="archive-doc__text">
          <button type="button" className="archive-doc__title" onClick={() => onView(node)}>
            {node.title}
          </button>
          <span className="archive-doc__meta">
            {[node.date && formatDate(node.date), `${node.files.length} tệp`].filter(Boolean).join(' · ')}
          </span>
        </span>
        <Tooltip title="Xem">
          <button
            type="button"
            className="archive-icon-btn"
            aria-label={`Xem ${node.title}`}
            onClick={() => onView(node)}
          >
            <Eye size={15} aria-hidden />
          </button>
        </Tooltip>
      </div>
      {node.files.length > 0 && (
        <div className="archive-doc__files">
          {node.files.map((f) => (
            <FileRow key={f.id} file={f} contractId={contractId} />
          ))}
        </div>
      )}
    </div>
  );
}

/** Documents of a stage; nested folders keep their own sub-heading. */
function StageDocs({
  nodes,
  contractId,
  onView,
}: {
  nodes: ArchiveNodeDto[];
  contractId: string;
  onView: (node: ArchiveNodeDto) => void;
}) {
  return (
    <>
      {nodes.map((n) =>
        n.kind === 'Folder' ? (
          <div key={n.key} className="archive-subfolder">
            <div className="archive-subfolder__title">{n.title}</div>
            <StageDocs nodes={n.children} contractId={contractId} onView={onView} />
          </div>
        ) : (
          <DocCard key={n.key} node={n} contractId={contractId} onView={onView} />
        ),
      )}
    </>
  );
}

function Dossier({ contractId }: { contractId: string }) {
  const tree = useArchiveTree(contractId);
  const t = tree.data;
  const [viewing, setViewing] = useState<ArchiveNodeDto | null>(null);
  const [downloading, setDownloading] = useState(false);
  const navigate = useNavigate();
  const { can } = useAuth();
  const { message } = App.useApp();
  const onError = useApiError();
  const complete = useArchiveComplete(contractId);

  if (!t)
    return (
      <section className="archive-dossier archive-dossier--empty">
        <Spin />
      </section>
    );

  return (
    <section className="archive-dossier">
      <header className="archive-dossier__head">
        <div className="archive-dossier__title">
          <div className="archive-dossier__code-row">
            <h2 className="archive-dossier__code mono">{t.contractNumber}</h2>
            <StatusTag domain="contractStatus" value={t.status} />
            {t.isArchiveComplete && (
              <span className="archive-dossier__complete">
                <ShieldCheck size={13} aria-hidden />
                Hồ sơ đầy đủ
              </span>
            )}
          </div>
          <div className="archive-dossier__name">{t.contractName}</div>
          <div className="archive-dossier__sub">
            {t.supplierName} · {t.bidDecisionNumber ? `QĐ ${t.bidDecisionNumber}` : 'ngoài thầu'} ·{' '}
            {t.fileCount} tệp
          </div>
        </div>
        {can(P.ArchiveDownload) && (
          <div className="archive-dossier__actions">
            <label className="archive-dossier__switch">
              <Switch
                size="small"
                checked={t.isArchiveComplete}
                loading={complete.isPending}
                onChange={(v) =>
                  complete
                    .mutateAsync(v)
                    .then(() => message.success(v ? 'Đã đánh dấu hồ sơ đầy đủ' : 'Đã bỏ đánh dấu'), onError)
                }
              />
              Đã đầy đủ
            </label>
            <Button
              type="primary"
              icon={<FolderDown size={16} aria-hidden />}
              loading={downloading}
              onClick={async () => {
                setDownloading(true);
                try {
                  await downloadFile(
                    `${ARCHIVE}/${contractId}/zip`,
                    `ho-so-${t.contractNumber.replace(/[^\w-]+/g, '-')}.zip`,
                    { timeout: 600_000 },
                  );
                  message.success('Đã tải hồ sơ (kèm muc_luc.pdf)');
                } catch (e) {
                  onError(e);
                } finally {
                  setDownloading(false);
                }
              }}
            >
              Tải toàn bộ hồ sơ (ZIP)
            </Button>
          </div>
        )}
      </header>
      {t.isArchiveComplete && (
        <div className="archive-dossier__banner">
          <Banner
            tone="success"
            title={`Hồ sơ đã được xác nhận đầy đủ bởi ${t.archiveCompletedByName} lúc ${formatDateTime(t.archiveCompletedAt)}`}
          />
        </div>
      )}
      <div className="archive-tree">
        {t.folders.map((g, i) => {
          const stage = STAGES[g.key] ?? { icon: FileText, tone: 'neutral' as Tone };
          const StageIcon = stage.icon;
          const count = g.children.length;
          return (
            <div key={g.key} className="archive-stage">
              <div className="archive-stage__rail">
                <span
                  className="archive-stage__icon"
                  style={{ background: `var(--bp-${stage.tone}-bg)`, color: `var(--bp-${stage.tone}-fg)` }}
                  aria-hidden
                >
                  <StageIcon size={15} strokeWidth={1.75} />
                </span>
                {i < t.folders.length - 1 && <span className="archive-stage__line" aria-hidden />}
              </div>
              <div className="archive-stage__body">
                <h3 className="archive-stage__title">
                  <span>{g.title}</span>
                  {count > 0 && <span className="archive-stage__count">{count} mục</span>}
                </h3>
                {count === 0 ? (
                  <div className="archive-stage__empty">Chưa có tài liệu ở giai đoạn này</div>
                ) : (
                  <StageDocs nodes={g.children} contractId={contractId} onView={setViewing} />
                )}
              </div>
            </div>
          );
        })}
      </div>
      <Drawer
        open={!!viewing}
        onClose={() => setViewing(null)}
        width="min(960px, 100vw)"
        title={viewing?.title}
        destroyOnHidden
        extra={
          viewing?.link && (
            <Button icon={<ExternalLink size={14} aria-hidden />} onClick={() => navigate(viewing.link!)}>
              Mở chi tiết
            </Button>
          )
        }
      >
        {viewing?.documentId ? (
          <DocumentPdf documentId={viewing.documentId} />
        ) : (
          <Empty description="Tài liệu không có bản PDF trên hệ thống — dùng nút tải tệp đính kèm." />
        )}
      </Drawer>
    </section>
  );
}
