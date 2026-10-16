import { Button, Grid, Modal, Drawer } from 'antd';
import { GitCompareArrows } from 'lucide-react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useInbox, type InboxItemDto } from '../../api/signing';
import { DataTable } from '../../components/DataTable';
import { StatusTag } from '../../components/StatusTag';
import { PageHeader } from '../../components/common';
import type { TableState } from '../../hooks/useUrlState';
import { dueLabel } from '../../utils/due';
import { formatDateTime } from '../../utils/format';
import { DocumentReview } from './SigningInboxPage';

const TABS = [
  { key: 'Pending', label: 'Chờ rà soát' },
  { key: 'Processed', label: 'Đã rà soát' },
] as const;

/**
 * Menu Rà soát (spec §9.1-6): documents waiting at a review step (contracts, appendices, acceptance, 08a, liquidation)
 * with the version comparison one click away.
 */
export function ReviewPage() {
  const [params, setParams] = useSearchParams();
  const navigate = useNavigate();
  const screens = Grid.useBreakpoint();
  const tab = params.get('tab') === 'Processed' ? 'Processed' : 'Pending';
  const page = Number(params.get('page') ?? 1) || 1;
  const pageSize = Number(params.get('pageSize') ?? 20) || 20;
  const search = params.get('search') ?? undefined;
  const openDocument = params.get('document');

  const set = (patch: Record<string, string | number | undefined>, resetPage = true) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        Object.entries(patch).forEach(([k, v]) =>
          v === undefined || v === '' ? next.delete(k) : next.set(k, String(v)),
        );
        if (resetPage && !('page' in patch)) next.delete('page');
        return next;
      },
      { replace: true },
    );

  const inboxQuery = { tab, page, pageSize, search, actionType: 'Review' } as const;
  const inbox = useInbox(inboxQuery);
  const state: TableState = { page, pageSize, search, filters: {} };
  const close = () => set({ document: undefined }, false);
  const review = openDocument && <DocumentReview documentId={openDocument} onDone={close} />;

  return (
    <>
      <PageHeader
        title="Rà soát"
        subtitle="Hợp đồng, phụ lục và biên bản chờ Tổ rà soát kiểm tra — so sánh các phiên bản trước khi đồng ý hoặc trả lại"
      />
      <DataTable<InboxItemDto>
        exportAs={{ title: 'Rà soát', url: '/api/signing/inbox', params: inboxQuery }}
        data={inbox.data}
        loading={inbox.isFetching}
        error={inbox.isError}
        onRetry={() => void inbox.refetch()}
        rowKey={(r) => r.stepId ?? r.instanceId}
        state={state}
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
        tabs={TABS.map((t) => ({
          key: t.key,
          label: t.label,
          count: t.key === tab ? inbox.data?.total : undefined,
        }))}
        activeTab={tab}
        onTabChange={(key) => set({ tab: key === 'Pending' ? undefined : key })}
        onRowClick={(r) => set({ document: r.documentId }, false)}
        emptyTitle={tab === 'Pending' ? 'Không có tài liệu chờ rà soát' : 'Chưa rà soát tài liệu nào'}
        emptyDescription="Tài liệu được gửi rà soát sẽ hiển thị tại đây."
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
          { title: 'Bước', dataIndex: 'stepName', render: (v: string | null) => v ?? '—' },
          {
            title: 'Nhà cung cấp',
            dataIndex: 'supplierName',
            width: 220,
            ellipsis: true,
            render: (v: string | null) => v ?? '—',
          },
          { title: 'Người gửi', dataIndex: 'startedByName', render: (v: string | null) => v ?? '—' },
          { title: 'Ngày gửi', dataIndex: 'startedAt', render: (v: string) => formatDateTime(v) },
          {
            title: 'Hạn xử lý',
            dataIndex: 'dueAt',
            render: (v: string | null, r) => {
              if (!v) return '—';
              if (tab !== 'Pending') return formatDateTime(v);
              const d = dueLabel(v, r.isOverdue);
              return <span style={{ color: `var(--bp-${d.tone}-fg)`, fontWeight: 500 }}>{d.text}</span>;
            },
          },
          {
            title: 'Trạng thái',
            dataIndex: 'workflowStatus',
            render: (v: string) => <StatusTag domain="workflowStatus" value={v} />,
          },
          {
            title: '',
            key: 'compare',
            render: (_, r) => (
              <Button
                size="small"
                icon={<GitCompareArrows size={14} aria-hidden />}
                onClick={(e) => {
                  e.stopPropagation();
                  navigate(`/tai-lieu/${r.documentId}?compare=1`);
                }}
              >
                So sánh phiên bản
              </Button>
            ),
          },
        ]}
      />
      {screens.lg ? (
        <Modal
          open={!!openDocument}
          onCancel={close}
          footer={null}
          width="96vw"
          style={{ top: 12 }}
          title="Rà soát tài liệu"
          destroyOnHidden
        >
          {review}
        </Modal>
      ) : (
        <Drawer open={!!openDocument} onClose={close} width="100%" title="Rà soát tài liệu" destroyOnHidden>
          {review}
        </Drawer>
      )}
    </>
  );
}
