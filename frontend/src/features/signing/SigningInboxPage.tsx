import {
  Alert,
  App,
  Badge,
  Button,
  Card,
  Col,
  DatePicker,
  Drawer,
  Dropdown,
  Empty,
  Grid,
  Input,
  Modal,
  Progress,
  Row,
  Select,
  Space,
  Table,
  Tabs,
  Tag,
  Typography,
} from 'antd';
import { ClockCircleOutlined, EditOutlined, FileSearchOutlined } from '@ant-design/icons';
import { useMemo, useState } from 'react';
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
import { PdfViewer } from '../../components/PdfViewer';
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { PageHeader } from '../../components/common';
import { formatDateTime } from '../../utils/format';
import { readTokenCertificate, signWithToken, tokenPluginConfigured } from './tokenPlugin';
import { waitForRemoteSignature } from './signingFlow';
import { WorkflowCard } from './WorkflowPanel';

const TABS: { key: InboxTab; label: string }[] = [
  { key: 'Pending', label: 'Chờ tôi xử lý' },
  { key: 'Processed', label: 'Tôi đã xử lý' },
  { key: 'Sent', label: 'Tôi đã gửi' },
  { key: 'Returned', label: 'Bị trả lại' },
];

/** PDF of the current version + workflow (steps, opinions, actions) side by side. */
function DocumentReview({ documentId, onDone }: { documentId: string; onDone: () => void }) {
  const doc = useDocument(documentId);
  const wf = useDocumentWorkflow(documentId);
  const navigate = useNavigate();
  const { isSupplier } = useAuth();
  const current = doc.data?.versions.find((v) => v.isCurrent);
  return (
    <Row gutter={[16, 16]}>
      <Col xs={24} xl={14}>
        <Card
          size="small"
          title={doc.data?.title ?? 'Tài liệu'}
          loading={doc.isLoading}
          extra={
            <Button
              size="small"
              icon={<FileSearchOutlined />}
              onClick={() => navigate(`${isSupplier ? '/ncc' : ''}/tai-lieu/${documentId}`)}
            >
              Chi tiết
            </Button>
          }
        >
          {current?.hasPdf ? (
            <PdfViewer
              load={() => fetchDocumentFile(documentId, current.id, 'pdf')}
              cacheKey={`${current.id}:${current.pdfSha256}`}
              height="70vh"
            />
          ) : (
            <Empty description="Tài liệu chưa có bản PDF" />
          )}
        </Card>
      </Col>
      <Col xs={24} xl={10}>
        {wf.data ? <WorkflowCard wf={wf.data} onDone={onDone} /> : <Card loading={wf.isLoading} />}
      </Col>
    </Row>
  );
}

interface BatchProgress {
  total: number;
  done: number;
  failed: { title: string; error: string }[];
  waitingPhone: number;
}

/** Menu Trình ký (spec §6.7): every file waiting for signature, for internal users and suppliers alike. */
export function SigningInboxPage() {
  const [params, setParams] = useSearchParams();
  const screens = Grid.useBreakpoint();
  const { isSupplier } = useAuth();
  const { message } = App.useApp();
  const tab = (params.get('tab') as InboxTab | null) ?? 'Pending';
  const page = Number(params.get('page') ?? 1) || 1;
  const pageSize = Number(params.get('pageSize') ?? 20) || 20;
  const search = params.get('search') ?? undefined;
  const documentType = (params.get('documentType') as DocumentType | null) ?? undefined;
  const supplierId = params.get('supplierId') ?? undefined;
  const from = params.get('from') ?? undefined;
  const to = params.get('to') ?? undefined;
  const openDocument = params.get('document');

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
  const batch = useBatchSign();
  const invalidate = useInvalidateWorkflow();
  const [selected, setSelected] = useState<string[]>([]);
  const [progress, setProgress] = useState<BatchProgress | null>(null);

  const rows = useMemo(() => inbox.data?.items ?? [], [inbox.data]);
  const signable = rows.filter((r) => r.requiresSignature && r.stepId);
  const selectedRows = rows.filter((r) => r.stepId && selected.includes(r.stepId));

  const runBatch = async (provider: SigningProviderDto) => {
    const targets = selectedRows.filter((r) => r.requiresSignature);
    if (targets.length === 0) return;
    const state: BatchProgress = { total: targets.length, done: 0, failed: [], waitingPhone: 0 };
    setProgress({ ...state });

    if (provider.mode === 'ClientSide') {
      // USB token: one token, documents signed one after another with a progress bar.
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
        const pending = results.filter((x) => x.success && x.request?.status === 'Pending');
        results
          .filter((x) => !x.success)
          .forEach((x) =>
            state.failed.push({
              title: targets.find((r) => r.stepId === x.stepId)?.documentTitle ?? '',
              error: x.errorMessage ?? 'Ký không thành công',
            }),
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
                title: targets.find((r) => r.stepId === x.stepId)?.documentTitle ?? '',
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
    setSelected([]);
    const ok = state.total - state.failed.length;
    if (state.failed.length === 0) message.success(`Đã ký ${ok}/${state.total} tài liệu`);
    else message.warning(`Đã ký ${ok}/${state.total} tài liệu, ${state.failed.length} tài liệu lỗi`);
  };

  const preferred = providers.data?.find((p) => p.isDefault) ?? providers.data?.[0];
  const reviewOpen = !!openDocument;

  return (
    <>
      <PageHeader
        title="Trình ký"
        subtitle="Toàn bộ tài liệu cần xử lý, ký số của anh/chị"
        extra={
          tab === 'Pending' &&
          selectedRows.length > 0 && (
            <Dropdown.Button
              type="primary"
              loading={batch.isPending || (progress !== null && progress.done < progress.total)}
              disabled={!preferred || selectedRows.every((r) => !r.requiresSignature)}
              onClick={() => preferred && void runBatch(preferred)}
              menu={{
                items: (providers.data ?? []).map((p) => ({ key: p.code, label: p.displayName })),
                onClick: ({ key }) => {
                  const p = providers.data?.find((x) => x.code === key);
                  if (p) void runBatch(p);
                },
              }}
            >
              <EditOutlined /> Ký hàng loạt ({selectedRows.filter((r) => r.requiresSignature).length})
            </Dropdown.Button>
          )
        }
      />

      <Card>
        <Tabs
          activeKey={tab}
          onChange={(key) => {
            setSelected([]);
            set({ tab: key === 'Pending' ? undefined : key });
          }}
          items={TABS.map((t) => ({
            key: t.key,
            label:
              t.key === 'Pending' ? (
                <Space size={6}>
                  {t.label}
                  <Badge count={count.data?.pending ?? 0} overflowCount={99} />
                  {(count.data?.overdue ?? 0) > 0 && (
                    <Tag color="red" icon={<ClockCircleOutlined />}>
                      {count.data?.overdue} quá hạn
                    </Tag>
                  )}
                </Space>
              ) : (
                t.label
              ),
          }))}
        />
        <Space wrap style={{ marginBottom: 12 }}>
          <Input.Search
            allowClear
            placeholder="Tìm theo tên tài liệu"
            defaultValue={search}
            onSearch={(v) => set({ search: v })}
            style={{ width: 260 }}
          />
          <Select
            allowClear
            placeholder="Loại tài liệu"
            style={{ width: 220 }}
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
              style={{ width: 260 }}
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
        </Space>

        {progress && (
          <Alert
            style={{ marginBottom: 12 }}
            type={progress.failed.length ? 'warning' : progress.done === progress.total ? 'success' : 'info'}
            closable={progress.done === progress.total}
            onClose={() => setProgress(null)}
            message={
              <Space direction="vertical" style={{ width: '100%' }}>
                <span>
                  Ký hàng loạt: {progress.done}/{progress.total}
                  {progress.waitingPhone > 0 &&
                    ` — đang chờ xác nhận ${progress.waitingPhone} yêu cầu trên điện thoại`}
                </span>
                <Progress percent={Math.round((progress.done / progress.total) * 100)} size="small" />
                {progress.failed.map((f, i) => (
                  <Typography.Text key={i} type="danger">
                    {f.title}: {f.error}
                  </Typography.Text>
                ))}
              </Space>
            }
          />
        )}

        <Table<InboxItemDto>
          rowKey={(r) => r.stepId ?? r.instanceId}
          size="middle"
          loading={inbox.isFetching}
          dataSource={rows}
          scroll={{ x: 'max-content' }}
          rowSelection={
            tab === 'Pending' && signable.length > 0
              ? {
                  selectedRowKeys: selected,
                  onChange: (keys) => setSelected(keys.map(String)),
                  getCheckboxProps: (r) => ({ disabled: !r.requiresSignature }),
                }
              : undefined
          }
          onRow={(r) => ({
            onClick: () => set({ document: r.documentId }, false),
            style: { cursor: 'pointer' },
          })}
          pagination={{
            current: page,
            pageSize,
            total: inbox.data?.total ?? 0,
            showSizeChanger: true,
            showTotal: (total) => `${total} tài liệu`,
            onChange: (p, s) =>
              set({ page: p === 1 ? undefined : p, pageSize: s === 20 ? undefined : s }, false),
          }}
          locale={{ emptyText: <Empty description="Không có tài liệu" /> }}
          columns={[
            {
              title: 'Tài liệu',
              dataIndex: 'documentTitle',
              render: (v: string, r) => (
                <Space direction="vertical" size={0}>
                  <Typography.Text strong>{v}</Typography.Text>
                  <StatusTag domain="documentType" value={r.documentType} />
                </Space>
              ),
            },
            {
              title: 'Bước',
              dataIndex: 'stepName',
              render: (v: string | null, r) =>
                v ? (
                  <Space direction="vertical" size={0}>
                    <span>{v}</span>
                    <StatusTag domain="workflowAction" value={r.actionType} />
                  </Space>
                ) : (
                  <StatusTag domain="workflowStatus" value={r.workflowStatus} />
                ),
            },
            ...(isSupplier ? [] : [{ title: 'Nhà cung cấp', dataIndex: 'supplierName' }]),
            { title: 'Người gửi', dataIndex: 'startedByName' },
            { title: 'Ngày gửi', dataIndex: 'startedAt', render: (v: string) => formatDateTime(v) },
            {
              title: 'Hạn xử lý',
              dataIndex: 'dueAt',
              render: (v: string | null, r) =>
                v ? (
                  <Typography.Text type={r.isOverdue ? 'danger' : undefined}>
                    {formatDateTime(v)}
                  </Typography.Text>
                ) : (
                  '—'
                ),
            },
            {
              title: 'Trạng thái',
              dataIndex: 'workflowStatus',
              render: (v: string) => <StatusTag domain="workflowStatus" value={v} />,
            },
          ]}
        />
      </Card>

      {screens.lg ? (
        <Modal
          open={reviewOpen}
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
          open={reviewOpen}
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
