import {
  Alert,
  App,
  Button,
  DatePicker,
  Drawer,
  Dropdown,
  Empty,
  Grid,
  Input,
  Modal,
  Progress,
  Select,
  Spin,
} from 'antd';
import {
  Check,
  ChevronDown,
  Clock,
  ExternalLink,
  FileText,
  LayoutList,
  PanelsTopLeft,
  PenLine,
  Search,
} from 'lucide-react';
import { useMemo, useState, type Key } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import dayjs, { APP_TIME_ZONE } from '../../i18n/dayjs';
import { useLookup } from '../../api/admin';
import { fetchDocumentFile, useDocument } from '../../api/documents';
import { toApiProblem } from '../../api/http';
import {
  beginSign,
  completeSigningRequest,
  useBatchSign,
  useDocumentWorkflow,
  useInbox,
  useInboxCount,
  useInvalidateWorkflow,
  useSigningProviders,
  type DocumentType,
  type InboxItemDto,
  type InboxTab,
  type SigningProviderDto,
} from '../../api/signing';
import { useAuth } from '../../auth/useAuth';
import { DataTable, type ActiveFilter } from '../../components/DataTable';
import { PdfViewer } from '../../components/PdfViewer';
import { StatusTag, statusLabel, statusOptions } from '../../components/StatusTag';
import { PageHeader } from '../../components/common';
import type { TableState } from '../../hooks/useUrlState';
import { dueLabel } from '../../utils/due';
import { formatDate, formatDateTime } from '../../utils/format';
import { readTokenCertificate, signWithToken, tokenPluginConfigured } from './tokenPlugin';
import { waitForRemoteSignature } from './signingFlow';
import { WorkflowActions, WorkflowCard } from './WorkflowPanel';
import { providerHint, providerIcon, usePreferredProvider } from './providers';
import './signingInbox.css';

type Mode = 'table' | 'quick';

const TABS: { key: InboxTab; label: string; supplierLabel: string }[] = [
  { key: 'Pending', label: 'Chờ tôi xử lý', supplierLabel: 'Chờ công ty xử lý' },
  { key: 'Processed', label: 'Tôi đã xử lý', supplierLabel: 'Đã xử lý' },
  { key: 'Sent', label: 'Tôi đã gửi', supplierLabel: 'Đã gửi' },
  { key: 'Returned', label: 'Bị trả lại', supplierLabel: 'Bị trả lại' },
];

/** PDF of the current version + workflow (steps, opinions, actions) side by side. */
function DocumentReview({ documentId, onDone }: { documentId: string; onDone: () => void }) {
  const doc = useDocument(documentId);
  const wf = useDocumentWorkflow(documentId);
  const navigate = useNavigate();
  const { isSupplier } = useAuth();
  const current = doc.data?.versions.find((v) => v.isCurrent);
  return (
    <div className="inbox-review">
      <section className="inbox-review__doc">
        <header className="inbox-review__head">
          <div className="inbox-review__title">
            <strong>{doc.data?.title ?? 'Tài liệu'}</strong>
            {doc.data && <StatusTag domain="documentType" value={doc.data.type} />}
          </div>
          <Button
            size="small"
            icon={<ExternalLink size={14} aria-hidden />}
            onClick={() => navigate(`${isSupplier ? '/ncc' : ''}/tai-lieu/${documentId}`)}
          >
            Mở chi tiết
          </Button>
        </header>
        {doc.isLoading ? (
          <Spin style={{ margin: 48 }} />
        ) : current?.hasPdf ? (
          <PdfViewer
            load={() => fetchDocumentFile(documentId, current.id, 'pdf')}
            cacheKey={`${current.id}:${current.pdfSha256}`}
            height="70vh"
            fileName={`${doc.data?.title ?? 'tai-lieu'}.pdf`}
          />
        ) : (
          <Empty description="Tài liệu chưa có bản PDF" style={{ margin: 48 }} />
        )}
      </section>
      <div>{wf.data ? <WorkflowCard wf={wf.data} onDone={onDone} /> : <Spin style={{ margin: 48 }} />}</div>
    </div>
  );
}

/** "Xem và xử lý tài liệu" mode (design/Trinh ky.dc.html): list on the left, PDF and the actions of my step on the right. */
function QuickReview({
  rows,
  loading,
  selectedId,
  onSelect,
  search,
  onSearch,
}: {
  rows: InboxItemDto[];
  loading: boolean;
  selectedId: string | null;
  onSelect: (documentId: string | null) => void;
  search?: string;
  onSearch: (value: string) => void;
}) {
  const navigate = useNavigate();
  const { isSupplier } = useAuth();
  const current = rows.find((r) => r.documentId === selectedId) ?? rows[0];
  const doc = useDocument(current?.documentId);
  const wf = useDocumentWorkflow(current?.documentId);
  const version = doc.data?.versions.find((v) => v.isCurrent);
  const due = current ? dueLabel(current.dueAt, current.isOverdue) : null;

  return (
    <section className="inbox-quick">
      <div className="inbox-quick__list">
        <div className="inbox-quick__search">
          <Input
            allowClear
            size="middle"
            prefix={<Search size={15} aria-hidden />}
            placeholder="Tìm tài liệu"
            aria-label="Tìm tài liệu"
            defaultValue={search}
            onPressEnter={(e) => onSearch((e.target as HTMLInputElement).value.trim())}
            onChange={(e) => !e.target.value && onSearch('')}
          />
        </div>
        <div className="inbox-quick__items" role="listbox" aria-label="Tài liệu">
          {loading && rows.length === 0 && <Spin style={{ margin: 24 }} />}
          {!loading && rows.length === 0 && (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description="Không có tài liệu"
              style={{ margin: 24 }}
            />
          )}
          {rows.map((r) => {
            const on = r === current;
            const d = dueLabel(r.dueAt, r.isOverdue);
            return (
              <button
                key={r.stepId ?? r.instanceId}
                type="button"
                role="option"
                aria-selected={on}
                className={`inbox-quick__item${on ? ' is-active' : ''}`}
                onClick={() => onSelect(r.documentId)}
              >
                <span className="inbox-quick__icon" aria-hidden>
                  <FileText size={15} strokeWidth={1.75} />
                </span>
                <span className="inbox-quick__text">
                  <span className="inbox-quick__name">{r.documentTitle}</span>
                  <span className="inbox-quick__type">{statusLabel('documentType', r.documentType)}</span>
                  {r.stepName && <span className="inbox-quick__step">{r.stepName}</span>}
                  {r.dueAt && (
                    <span className="inbox-quick__due" style={{ color: `var(--bp-${d.tone}-fg)` }}>
                      {d.text}
                    </span>
                  )}
                </span>
              </button>
            );
          })}
        </div>
      </div>
      <div className="inbox-quick__view">
        {!current ? (
          <Empty description="Chọn một tài liệu để xem" style={{ margin: 'auto' }} />
        ) : (
          <>
            <header className="inbox-quick__head">
              <div className="inbox-quick__head-text">
                <div className="inbox-quick__head-title">{current.documentTitle}</div>
                <div className="inbox-quick__head-sub">
                  {statusLabel('documentType', current.documentType)}
                  {current.stepName && ` · ${current.stepName}`}
                  {current.startedByName &&
                    ` · ${current.startedByName} gửi ${formatDate(current.startedAt)}`}
                </div>
              </div>
              <Button
                size="small"
                icon={<ExternalLink size={14} aria-hidden />}
                onClick={() => navigate(`${isSupplier ? '/ncc' : ''}/tai-lieu/${current.documentId}`)}
              >
                Mở chi tiết
              </Button>
            </header>
            <div className="inbox-quick__pdf">
              {doc.isLoading ? (
                <Spin style={{ margin: 48 }} />
              ) : version?.hasPdf ? (
                <PdfViewer
                  load={() => fetchDocumentFile(current.documentId, version.id, 'pdf')}
                  cacheKey={`${version.id}:${version.pdfSha256}`}
                  height="62vh"
                  fileName={`${current.documentTitle}.pdf`}
                />
              ) : (
                <Empty description="Tài liệu chưa có bản PDF" style={{ margin: 48 }} />
              )}
            </div>
            <footer className="inbox-quick__actions">
              {due && current.dueAt && (
                <span className="inbox-quick__deadline" style={{ color: `var(--bp-${due.tone}-fg)` }}>
                  <Clock size={14} aria-hidden />
                  {due.text}
                </span>
              )}
              <span className="inbox-quick__spacer" />
              {wf.data ? (
                wf.data.myStepId ? (
                  <WorkflowActions wf={wf.data} part="mine" onDone={() => onSelect(null)} />
                ) : (
                  <StatusTag domain="workflowStatus" value={wf.data.status} />
                )
              ) : (
                <Spin size="small" />
              )}
            </footer>
          </>
        )}
      </div>
    </section>
  );
}

interface BatchProgress {
  total: number;
  done: number;
  failed: { title: string; error: string }[];
  waitingPhone: number;
}

/** Signs several inbox rows with one provider: USB token one after another, remote providers in one batch. */
function useBatchSigning() {
  const batch = useBatchSign();
  const invalidate = useInvalidateWorkflow();
  const { message } = App.useApp();
  const [progress, setProgress] = useState<BatchProgress | null>(null);

  const run = async (provider: SigningProviderDto, targets: InboxItemDto[]) => {
    if (targets.length === 0) return;
    const state: BatchProgress = { total: targets.length, done: 0, failed: [], waitingPhone: 0 };
    setProgress({ ...state });

    if (provider.mode === 'ClientSide') {
      if (!tokenPluginConfigured) {
        message.warning('Ký hàng loạt bằng USB token cần plugin ký số của CA. Vui lòng ký từng tài liệu.');
        setProgress(null);
        return;
      }
      try {
        const certificate = await readTokenCertificate();
        for (const r of targets) {
          try {
            const request = await beginSign(r.stepId!, {
              provider: provider.code,
              clientCertificate: certificate,
            });
            const result = request.dataToSign
              ? await completeSigningRequest(request.requestId, await signWithToken(request.dataToSign))
              : request;
            if (result.status !== 'Completed')
              state.failed.push({ title: r.documentTitle, error: result.error ?? 'Ký không thành công' });
          } catch (e) {
            state.failed.push({
              title: r.documentTitle,
              error: e instanceof Error ? e.message : toApiProblem(e).title,
            });
          }
          state.done += 1;
          setProgress({ ...state });
        }
      } catch (e) {
        message.error(e instanceof Error ? e.message : 'Không đọc được chứng thư từ USB token.');
      }
    } else {
      try {
        const results = await batch.mutateAsync({
          stepIds: targets.map((r) => r.stepId!),
          provider: provider.code,
        });
        const titleOf = (stepId: string) => targets.find((r) => r.stepId === stepId)?.documentTitle ?? '';
        const pending = results.filter((x) => x.success && x.request?.status === 'Pending');
        results
          .filter((x) => !x.success)
          .forEach((x) =>
            state.failed.push({ title: titleOf(x.stepId), error: x.errorMessage ?? 'Ký không thành công' }),
          );
        state.done = results.length - pending.length;
        state.waitingPhone = pending.length;
        setProgress({ ...state });
        // Remote signing: one confirmation per document on the phone; poll them together.
        await Promise.all(
          pending.map(async (x) => {
            const result = await waitForRemoteSignature(x.request!, () => false).catch(() => null);
            if (result?.status !== 'Completed') {
              state.failed.push({
                title: titleOf(x.stepId),
                error: result?.error ?? 'Chưa xác nhận trên điện thoại',
              });
            }
            state.done += 1;
            state.waitingPhone -= 1;
            setProgress({ ...state });
          }),
        );
      } catch (e) {
        message.error(toApiProblem(e).title);
      }
    }

    await invalidate();
    const ok = state.total - state.failed.length;
    if (state.failed.length === 0) message.success(`Đã ký ${ok}/${state.total} tài liệu`);
    else message.warning(`Đã ký ${ok}/${state.total} tài liệu, ${state.failed.length} tài liệu lỗi`);
  };

  const running = batch.isPending || (progress !== null && progress.done < progress.total);
  return { run, progress, running, dismiss: () => setProgress(null) };
}

/** Menu Trình ký (spec §6.7): every file waiting for signature, for internal users and suppliers alike. */
export function SigningInboxPage() {
  const [params, setParams] = useSearchParams();
  const screens = Grid.useBreakpoint();
  const { isSupplier } = useAuth();
  const tab = (params.get('tab') as InboxTab | null) ?? 'Pending';
  const mode: Mode = params.get('view') === 'quick' ? 'quick' : 'table';
  const page = Number(params.get('page') ?? 1) || 1;
  const pageSize = Number(params.get('pageSize') ?? 20) || 20;
  const search = params.get('search') ?? undefined;
  const documentType = (params.get('documentType') as DocumentType | null) ?? undefined;
  const supplierId = params.get('supplierId') ?? undefined;
  const from = params.get('from') ?? undefined;
  const to = params.get('to') ?? undefined;
  const openDocument = params.get('document');
  const selectedDocument = params.get('selected');

  const set = (patch: Record<string, string | number | undefined | null>, resetPage = true) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        Object.entries(patch).forEach(([k, v]) =>
          v === undefined || v === null || v === '' ? next.delete(k) : next.set(k, String(v)),
        );
        if (resetPage && !('page' in patch)) next.delete('page');
        return next;
      },
      { replace: true },
    );

  const inbox = useInbox({
    tab,
    page,
    pageSize,
    search,
    documentType,
    supplierId,
    from: from ? dayjs.tz(from, APP_TIME_ZONE).startOf('day').toISOString() : undefined,
    to: to ? dayjs.tz(to, APP_TIME_ZONE).add(1, 'day').startOf('day').toISOString() : undefined,
  });
  const count = useInboxCount(true);
  const suppliers = useLookup('suppliers', undefined, !isSupplier);
  const providers = useSigningProviders();
  const { provider, choose } = usePreferredProvider(providers.data);
  const batch = useBatchSigning();

  const rows = useMemo(() => inbox.data?.items ?? [], [inbox.data]);
  const tableState: TableState = { page, pageSize, search, filters: {} };
  const supplierName = suppliers.data?.find((s) => s.id === supplierId)?.name;
  const chips: ActiveFilter[] = [
    ...(documentType
      ? [
          {
            key: 'documentType',
            label: `Loại: ${statusLabel('documentType', documentType)}`,
            onRemove: () => set({ documentType: undefined }),
          },
        ]
      : []),
    ...(supplierId
      ? [
          {
            key: 'supplier',
            label: `NCC: ${supplierName ?? '…'}`,
            onRemove: () => set({ supplierId: undefined }),
          },
        ]
      : []),
    ...(from && to
      ? [
          {
            key: 'date',
            label: `Gửi ${formatDate(from)} – ${formatDate(to)}`,
            onRemove: () => set({ from: undefined, to: undefined }),
          },
        ]
      : []),
  ];

  const filters = (
    <>
      <Select
        allowClear
        placeholder="Loại tài liệu"
        style={{ width: 200 }}
        value={documentType}
        options={statusOptions('documentType')}
        onChange={(v) => set({ documentType: v })}
      />
      {!isSupplier && (
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder="Nhà cung cấp"
          style={{ width: 240 }}
          value={supplierId}
          options={(suppliers.data ?? []).map((s) => ({ value: s.id, label: s.name }))}
          onChange={(v) => set({ supplierId: v })}
        />
      )}
      <DatePicker.RangePicker
        format="DD/MM/YYYY"
        placeholder={['Gửi từ ngày', 'Đến ngày']}
        value={from && to ? [dayjs(from), dayjs(to)] : null}
        onChange={(v) => set({ from: v?.[0]?.format('YYYY-MM-DD'), to: v?.[1]?.format('YYYY-MM-DD') })}
      />
    </>
  );

  const providerPicker = (providers.data?.length ?? 0) > 0 && (
    <Dropdown
      trigger={['click']}
      menu={{
        selectable: true,
        selectedKeys: provider ? [provider.code] : [],
        onClick: ({ key }) => choose(key),
        items: [
          { type: 'group', label: 'Nhà cung cấp chữ ký số' },
          ...(providers.data ?? []).map((p) => ({
            key: p.code,
            icon: providerIcon(p, 16),
            label: (
              <span className="inbox-provider">
                <span className="inbox-provider__name">{p.displayName}</span>
                <span className="inbox-provider__hint">{providerHint(p)}</span>
              </span>
            ),
            extra: p.code === provider?.code ? <Check size={16} aria-hidden /> : undefined,
          })),
        ],
      }}
    >
      <Button aria-label="Nhà cung cấp chữ ký số">
        {providerIcon(provider, 16)}
        {provider?.displayName}
        <ChevronDown size={15} aria-hidden />
      </Button>
    </Dropdown>
  );

  const bulkSign = (keys: Key[], clear: () => void) => {
    const targets = rows.filter((r) => r.requiresSignature && r.stepId && keys.includes(r.stepId));
    return (
      <Button
        type="primary"
        size="small"
        icon={<PenLine size={14} aria-hidden />}
        loading={batch.running}
        disabled={!provider || targets.length === 0}
        onClick={async () => {
          await batch.run(provider!, targets);
          clear();
        }}
      >
        Ký hàng loạt ({targets.length}) · {provider?.displayName}
      </Button>
    );
  };

  const progress = batch.progress;
  const pendingCount = count.data?.pending;

  return (
    <>
      <PageHeader
        title="Trình ký"
        subtitle={
          isSupplier
            ? 'Tài liệu bệnh viện gửi công ty rà soát và ký số'
            : 'Toàn bộ tài liệu cần xử lý, ký số của anh/chị'
        }
        extra={
          <div className="inbox-toolbar">
            {providerPicker}
            <div role="radiogroup" aria-label="Chế độ xem" className="inbox-modes">
              {(
                [
                  ['table', 'Danh sách', LayoutList],
                  ['quick', 'Xem và xử lý tài liệu', PanelsTopLeft],
                ] as const
              ).map(([key, label, Icon]) => (
                <button
                  key={key}
                  type="button"
                  role="radio"
                  aria-checked={mode === key}
                  className={`inbox-modes__item${mode === key ? ' is-active' : ''}`}
                  onClick={() => set({ view: key === 'table' ? undefined : key, selected: undefined }, false)}
                >
                  <Icon size={15} strokeWidth={1.75} aria-hidden />
                  {label}
                </button>
              ))}
            </div>
          </div>
        }
      />

      {progress && (
        <Alert
          style={{ marginBottom: 12 }}
          type={progress.failed.length ? 'warning' : progress.done === progress.total ? 'success' : 'info'}
          closable={progress.done === progress.total}
          onClose={batch.dismiss}
          message={
            <div className="inbox-progress">
              <span>
                Ký hàng loạt: {progress.done}/{progress.total}
                {progress.waitingPhone > 0 &&
                  ` — đang chờ xác nhận ${progress.waitingPhone} yêu cầu trên điện thoại`}
              </span>
              <Progress percent={Math.round((progress.done / progress.total) * 100)} size="small" />
              {progress.failed.map((f, i) => (
                <span key={i} className="inbox-progress__error">
                  {f.title}: {f.error}
                </span>
              ))}
            </div>
          }
        />
      )}

      {mode === 'table' ? (
        <DataTable<InboxItemDto>
          data={inbox.data}
          loading={inbox.isFetching}
          error={inbox.isError}
          onRetry={() => void inbox.refetch()}
          rowKey={(r) => r.stepId ?? r.instanceId}
          state={tableState}
          onChange={(patch, resetPage) =>
            set(
              {
                ...('search' in patch ? { search: patch.search } : {}),
                ...('page' in patch ? { page: patch.page === 1 ? undefined : patch.page } : {}),
                ...('pageSize' in patch
                  ? { pageSize: patch.pageSize === 20 ? undefined : patch.pageSize }
                  : {}),
              },
              resetPage,
            )
          }
          searchPlaceholder="Tìm theo tên tài liệu"
          filters={filters}
          activeFilters={chips}
          onClearFilters={() =>
            set({ documentType: undefined, supplierId: undefined, from: undefined, to: undefined })
          }
          tabs={TABS.map((t) => ({
            key: t.key,
            label: isSupplier ? t.supplierLabel : t.label,
            count: t.key === 'Pending' ? pendingCount : t.key === tab ? inbox.data?.total : undefined,
          }))}
          activeTab={tab}
          onTabChange={(key) => set({ tab: key === 'Pending' ? undefined : key })}
          bulkActions={tab === 'Pending' ? bulkSign : undefined}
          rowSelectable={(r) => r.requiresSignature && !!r.stepId}
          onRowClick={(r) => set({ document: r.documentId }, false)}
          emptyTitle="Không có tài liệu"
          emptyDescription={
            tab === 'Pending'
              ? 'Anh/chị đã xử lý hết tài liệu được giao.'
              : 'Không có tài liệu nào trong mục này.'
          }
          columns={[
            {
              title: 'Tài liệu',
              dataIndex: 'documentTitle',
              width: 360,
              render: (v: string, r) => (
                <span className="inbox-doc">
                  <span className="inbox-doc__title">{v}</span>
                  <StatusTag domain="documentType" value={r.documentType} />
                </span>
              ),
            },
            {
              title: 'Bước',
              dataIndex: 'stepName',
              render: (v: string | null, r) =>
                v ? (
                  <span className="inbox-doc">
                    <span>{v}</span>
                    <StatusTag domain="workflowAction" value={r.actionType} />
                  </span>
                ) : (
                  '—'
                ),
            },
            ...(isSupplier
              ? []
              : [
                  {
                    title: 'Nhà cung cấp',
                    dataIndex: 'supplierName',
                    width: 240,
                    ellipsis: true,
                    render: (v: string | null) => v ?? '—',
                  },
                ]),
            { title: 'Người gửi', dataIndex: 'startedByName', render: (v: string | null) => v ?? '—' },
            { title: 'Ngày gửi', dataIndex: 'startedAt', render: (v: string) => formatDateTime(v) },
            {
              title: 'Hạn xử lý',
              dataIndex: 'dueAt',
              render: (v: string | null, r) => {
                if (!v) return '—';
                if (tab !== 'Pending') return formatDateTime(v);
                const d = dueLabel(v, r.isOverdue);
                return (
                  <span
                    className="inbox-due"
                    style={{ color: `var(--bp-${d.tone}-fg)` }}
                    title={formatDateTime(v)}
                  >
                    {d.text}
                  </span>
                );
              },
            },
            {
              title: 'Trạng thái',
              dataIndex: 'workflowStatus',
              render: (v: string) => <StatusTag domain="workflowStatus" value={v} />,
            },
          ]}
        />
      ) : (
        <>
          <div role="tablist" className="data-table__tabs">
            {TABS.map((t) => (
              <button
                key={t.key}
                type="button"
                role="tab"
                aria-selected={t.key === tab}
                className={`data-table__tab${t.key === tab ? ' data-table__tab--active' : ''}`}
                onClick={() => set({ tab: t.key === 'Pending' ? undefined : t.key, selected: undefined })}
              >
                {isSupplier ? t.supplierLabel : t.label}
                {t.key === 'Pending' && pendingCount !== undefined && (
                  <span className="data-table__tab-count">{pendingCount}</span>
                )}
              </button>
            ))}
          </div>
          <QuickReview
            rows={rows}
            loading={inbox.isFetching}
            selectedId={selectedDocument}
            onSelect={(id) => set({ selected: id }, false)}
            search={search}
            onSearch={(v) => set({ search: v, selected: undefined })}
          />
        </>
      )}

      {screens.lg ? (
        <Modal
          open={!!openDocument}
          onCancel={() => set({ document: undefined }, false)}
          footer={null}
          width="96vw"
          style={{ top: 12 }}
          title="Xem và xử lý tài liệu"
          destroyOnHidden
        >
          {openDocument && <DocumentReview documentId={openDocument} onDone={() => undefined} />}
        </Modal>
      ) : (
        <Drawer
          open={!!openDocument}
          onClose={() => set({ document: undefined }, false)}
          width="100%"
          title="Xem và xử lý tài liệu"
          destroyOnHidden
        >
          {openDocument && <DocumentReview documentId={openDocument} onDone={() => undefined} />}
        </Drawer>
      )}
    </>
  );
}
