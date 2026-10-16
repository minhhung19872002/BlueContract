import { App, Button, Card, Descriptions, Drawer, Select, Space, Tag, Typography } from 'antd';
import { CloudDownloadOutlined, RedoOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { usePagedList } from '../../api/admin';
import {
  INTEGRATION,
  integrationApi,
  useIntegrationConfig,
  useIntegrationLog,
  useReceiptAction,
  type IntegrationLogListItemDto,
} from '../../api/receipts';
import { DataTable } from '../../components/DataTable';
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { PageHeader, useApiError } from '../../components/common';
import { useTableUrlState } from '../../hooks/useUrlState';
import { formatDateTime } from '../../utils/format';

const FILTERS = ['direction', 'status', 'operation'] as const;

const OPERATIONS: Record<string, string> = {
  PushWarehouseReceipt: 'HIS đẩy phiếu nhập kho',
  PullWarehouseReceipts: 'Cổng kéo phiếu từ HIS',
  GetContracts: 'HIS lấy hợp đồng',
  GetProposalSummaries: 'HIS lấy tổng hợp đề xuất',
  GetItems: 'HIS lấy danh mục hàng hóa',
};

const pretty = (text?: string | null) => {
  if (!text) return '—';
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return text;
  }
};

/** Cấu hình tích hợp HIS + nhật ký request/response, gửi lại, kéo phiếu ngay (spec §7.2, §9.1-14). */
export function IntegrationPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS);
  const logs = usePagedList<IntegrationLogListItemDto>(`${INTEGRATION}/logs`, apiParams);
  const config = useIntegrationConfig();
  const [selected, setSelected] = useState<string | null>(null);
  const { message } = App.useApp();
  const onError = useApiError();
  const pull = useReceiptAction(() => integrationApi.pull());
  const c = config.data;

  return (
    <>
      <PageHeader
        title="Tích hợp HIS"
        subtitle="API cho HIS (API key + chữ ký HMAC-SHA256), kéo phiếu định kỳ và nhật ký tích hợp"
        extra={
          <Button
            type="primary"
            icon={<CloudDownloadOutlined />}
            loading={pull.isPending}
            onClick={() =>
              pull
                .mutateAsync()
                .then(
                  (r) =>
                    message.success(
                      `Kéo ${r.received} phiếu: ${r.created} mới, ${r.updated} cập nhật, ${r.duplicates} trùng, ${r.failed} lỗi`,
                    ),
                  onError,
                )
            }
          >
            Kéo phiếu từ HIS ngay
          </Button>
        }
      />
      <Card style={{ marginBottom: 16 }} loading={config.isLoading}>
        {c && (
          <Descriptions size="small" column={{ xs: 1, md: 3 }}>
            <Descriptions.Item label="Hệ thống HIS">
              {c.displayName} <Tag>{c.provider}</Tag>
            </Descriptions.Item>
            <Descriptions.Item label="API cho HIS">
              {c.inboundEnabled ? <Tag color="green">Đang bật</Tag> : <Tag>Chưa cấu hình API key</Tag>}
            </Descriptions.Item>
            <Descriptions.Item label="Ứng dụng được cấp key">{c.clients.join(', ') || '—'}</Descriptions.Item>
            <Descriptions.Item label="IP được phép">
              {c.allowedIps.filter(Boolean).join(', ') || 'Mọi IP'}
            </Descriptions.Item>
            <Descriptions.Item label="Giới hạn">{c.rateLimitPerMinute} request/phút</Descriptions.Item>
            <Descriptions.Item label="Kéo phiếu định kỳ">
              {c.pullEnabled ? (
                <Space size={4}>
                  <Tag color="green">Bật</Tag>
                  <Typography.Text code>{c.pullCron}</Typography.Text>
                  <span>({c.pullLookbackDays} ngày gần nhất)</span>
                </Space>
              ) : (
                <Tag>Tắt</Tag>
              )}
            </Descriptions.Item>
            <Descriptions.Item label="Giả lập HIS (dev)">
              {c.devEndpoints ? <Tag color="orange">Bật</Tag> : <Tag>Tắt</Tag>}
            </Descriptions.Item>
            <Descriptions.Item label="Tài liệu" span={2}>
              docs/HIS_INTEGRATION.md — endpoint, cách ký request, mẫu payload, mã lỗi
            </Descriptions.Item>
          </Descriptions>
        )}
      </Card>
      <DataTable<IntegrationLogListItemDto>
        data={logs.data}
        loading={logs.isFetching}
        state={state}
        onChange={update}
        searchPlaceholder="Tìm theo mã phiếu HIS, đường dẫn, mã lỗi"
        filters={
          <>
            <Select
              allowClear
              placeholder="Chiều"
              style={{ width: 150 }}
              value={state.filters.direction}
              options={statusOptions('integrationDirection')}
              onChange={(v) => update({ filters: { direction: v } })}
            />
            <Select
              allowClear
              placeholder="Kết quả"
              style={{ width: 150 }}
              value={state.filters.status}
              options={statusOptions('integrationStatus')}
              onChange={(v) => update({ filters: { status: v } })}
            />
            <Select
              allowClear
              placeholder="Nghiệp vụ"
              style={{ width: 230 }}
              value={state.filters.operation}
              options={Object.entries(OPERATIONS).map(([value, label]) => ({ value, label }))}
              onChange={(v) => update({ filters: { operation: v } })}
            />
          </>
        }
        columns={[
          { title: 'Thời điểm', dataIndex: 'startedAt', render: (v: string) => formatDateTime(v) },
          {
            title: 'Chiều',
            dataIndex: 'direction',
            render: (v: string) => <StatusTag domain="integrationDirection" value={v} />,
          },
          {
            title: 'Nghiệp vụ',
            dataIndex: 'operation',
            render: (v: string, r) => <a onClick={() => setSelected(r.id)}>{OPERATIONS[v] ?? v}</a>,
          },
          { title: 'Mã phiếu HIS', dataIndex: 'externalId', render: (v?: string | null) => v ?? '—' },
          {
            title: 'Kết quả',
            dataIndex: 'status',
            render: (v: string, r) => (
              <Space size={2}>
                <StatusTag domain="integrationStatus" value={v} />
                {r.responseStatus && (
                  <Typography.Text type="secondary">HTTP {r.responseStatus}</Typography.Text>
                )}
              </Space>
            ),
          },
          { title: 'Mã lỗi', dataIndex: 'errorCode', render: (v?: string | null) => v ?? '' },
          { title: 'Lần', dataIndex: 'attempts', align: 'center' as const },
          { title: 'Nguồn', key: 'client', render: (_, r) => r.clientName ?? r.remoteIp ?? '—' },
          {
            title: 'Thời gian xử lý',
            dataIndex: 'durationMs',
            align: 'right' as const,
            render: (v: number) => `${v} ms`,
          },
        ]}
      />
      <LogDrawer id={selected} onClose={() => setSelected(null)} />
    </>
  );
}

function LogDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const log = useIntegrationLog(id);
  const l = log.data;
  const navigate = useNavigate();
  const { message } = App.useApp();
  const onError = useApiError();
  const retry = useReceiptAction(() => integrationApi.retry(id!));

  return (
    <Drawer
      title="Chi tiết lần gọi"
      width={760}
      open={!!id}
      onClose={onClose}
      extra={
        l?.canRetry && (
          <Button
            type="primary"
            icon={<RedoOutlined />}
            loading={retry.isPending}
            onClick={() =>
              retry
                .mutateAsync()
                .then((r) => message.success(`Đã xử lý lại: phiếu ${r.receiptNumber}`), onError)
            }
          >
            Gửi lại
          </Button>
        )
      }
    >
      {l && (
        <Space direction="vertical" style={{ width: '100%' }}>
          <Descriptions size="small" column={2} bordered>
            <Descriptions.Item label="Nghiệp vụ" span={2}>
              {OPERATIONS[l.operation] ?? l.operation}
            </Descriptions.Item>
            <Descriptions.Item label="Yêu cầu" span={2}>
              <Typography.Text code>
                {l.method} {l.path}
              </Typography.Text>
            </Descriptions.Item>
            <Descriptions.Item label="Kết quả">
              <StatusTag domain="integrationStatus" value={l.status} />{' '}
              {l.responseStatus && `HTTP ${l.responseStatus}`}
            </Descriptions.Item>
            <Descriptions.Item label="Số lần xử lý">{l.attempts}</Descriptions.Item>
            <Descriptions.Item label="Bắt đầu">{formatDateTime(l.startedAt)}</Descriptions.Item>
            <Descriptions.Item label="Lần gần nhất">{formatDateTime(l.lastAttemptAt)}</Descriptions.Item>
            <Descriptions.Item label="Nguồn">
              {l.clientName ?? '—'} {l.remoteIp && `(${l.remoteIp})`}
            </Descriptions.Item>
            <Descriptions.Item label="Phiếu">
              {l.receiptId ? (
                <a onClick={() => navigate(`/nhap-kho/${l.receiptId}`)}>Mở phiếu nhập kho</a>
              ) : (
                '—'
              )}
            </Descriptions.Item>
            {l.errorCode && (
              <Descriptions.Item label="Lỗi" span={2}>
                <Typography.Text type="danger">
                  {l.errorCode}: {l.errorMessage}
                </Typography.Text>
              </Descriptions.Item>
            )}
          </Descriptions>
          <Typography.Title level={5}>Nội dung gửi (request)</Typography.Title>
          <pre style={{ maxHeight: 320, overflow: 'auto', background: '#fafafa', padding: 12, fontSize: 12 }}>
            {pretty(l.requestBody)}
          </pre>
          <Typography.Title level={5}>Kết quả trả về (response)</Typography.Title>
          <pre style={{ maxHeight: 320, overflow: 'auto', background: '#fafafa', padding: 12, fontSize: 12 }}>
            {pretty(l.responseBody)}
          </pre>
        </Space>
      )}
    </Drawer>
  );
}
