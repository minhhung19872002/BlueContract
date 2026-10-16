import { Alert, App, Button, Card, Drawer, Table, Tag, Typography } from 'antd';
import { SafetyCertificateOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { usePagedList } from '../../api/admin';
import { INTEGRITY, useIntegrityRun, useRunIntegrityCheck } from '../../api/reports';
import type { components } from '../../api/schema';
import { DataTable } from '../../components/DataTable';
import { PageHeader, useApiError } from '../../components/common';
import { useTableUrlState } from '../../hooks/useUrlState';
import { formatDateTime } from '../../utils/format';

type RunItem = components['schemas']['IntegrityRunListItemDto'];

const size = (bytes: number) =>
  bytes > 1024 * 1024
    ? `${(bytes / 1024 / 1024).toLocaleString('vi-VN', { maximumFractionDigits: 1 })} MB`
    : `${Math.round(bytes / 1024)} KB`;

/** Kiểm tra toàn vẹn tài liệu (spec §11): job hằng ngày băm lại mọi tệp và so với SHA-256 đã lưu; chạy ngay khi cần. */
export function IntegrityPage() {
  const { state, update, apiParams } = useTableUrlState([] as const);
  const runs = usePagedList<RunItem>(INTEGRITY, apiParams);
  const [selected, setSelected] = useState<string | null>(null);
  const { message } = App.useApp();
  const onError = useApiError();
  const runCheck = useRunIntegrityCheck();
  const run = {
    isPending: runCheck.isPending,
    mutate: () =>
      runCheck.mutate(undefined, {
        onSuccess: (r) => {
          if (r.run.isHealthy) message.success(`Đã kiểm tra ${r.run.filesChecked} tệp — tất cả khớp`);
          else
            message.warning(`Phát hiện ${r.run.mismatchCount} tệp sai mã, ${r.run.missingCount} tệp thiếu`);
        },
        onError,
      }),
  };
  const detail = useIntegrityRun(selected);

  return (
    <>
      <PageHeader
        title="Kiểm tra toàn vẹn tài liệu"
        subtitle="Mỗi ngày lúc 02:00 hệ thống tải lại mọi tệp đã lưu, tính SHA-256 và so với giá trị ghi khi tải lên"
        extra={
          <Button
            type="primary"
            icon={<SafetyCertificateOutlined />}
            loading={run.isPending}
            onClick={() => run.mutate()}
          >
            Kiểm tra ngay
          </Button>
        }
      />
      <DataTable<RunItem>
        exportAs={{ title: 'Kiểm tra toàn vẹn', url: INTEGRITY, params: apiParams }}
        data={runs.data}
        loading={runs.isFetching}
        state={state}
        onChange={update}
        columns={[
          {
            title: 'Bắt đầu',
            dataIndex: 'startedAt',
            render: (v: string, r) => <a onClick={() => setSelected(r.id)}>{formatDateTime(v)}</a>,
          },
          { title: 'Kích hoạt bởi', dataIndex: 'trigger' },
          { title: 'Số tệp', dataIndex: 'filesChecked', align: 'right' as const },
          {
            title: 'Dung lượng',
            dataIndex: 'bytesChecked',
            align: 'right' as const,
            render: (v: number) => size(v),
          },
          {
            title: 'Kết quả',
            key: 'result',
            render: (_: unknown, r: RunItem) =>
              !r.finishedAt ? (
                <Tag color="processing">Đang chạy</Tag>
              ) : r.isHealthy ? (
                <Tag color="green">Tất cả khớp</Tag>
              ) : (
                <Tag color="red">
                  {r.error ? 'Lỗi' : `${r.mismatchCount} sai mã, ${r.missingCount} thiếu`}
                </Tag>
              ),
          },
        ]}
      />
      <Drawer title="Chi tiết lần kiểm tra" width={900} open={!!selected} onClose={() => setSelected(null)}>
        {detail.data && (
          <Card size="small">
            {detail.data.run.error && (
              <Alert type="error" showIcon message={detail.data.run.error} style={{ marginBottom: 12 }} />
            )}
            {detail.data.issues.length === 0 ? (
              <Alert
                type="success"
                showIcon
                message={`Đã kiểm tra ${detail.data.run.filesChecked} tệp — tất cả khớp.`}
              />
            ) : (
              <Table
                rowKey="objectKey"
                size="small"
                pagination={false}
                dataSource={detail.data.issues}
                columns={[
                  { title: 'Nguồn', dataIndex: 'source' },
                  { title: 'Tài liệu', dataIndex: 'title' },
                  {
                    title: 'Lỗi',
                    key: 'kind',
                    render: (_, i) =>
                      i.missing ? (
                        <Tag color="volcano">Không đọc được</Tag>
                      ) : (
                        <Tag color="red">Sai SHA-256</Tag>
                      ),
                  },
                  {
                    title: 'SHA-256 lưu / thực tế',
                    key: 'hash',
                    render: (_, i) => (
                      <Typography.Text code style={{ fontSize: 11 }}>
                        {i.expectedSha256.slice(0, 16)}… / {i.actualSha256?.slice(0, 16) ?? '—'}…
                      </Typography.Text>
                    ),
                  },
                ]}
              />
            )}
          </Card>
        )}
      </Drawer>
    </>
  );
}
