import { Alert, App, Button, Card, Checkbox, Empty, Form, Input, Select, Space, Table } from 'antd';
import { PlusOutlined, SaveOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import dayjs from '../../i18n/dayjs';
import { usePagedList } from '../../api/admin';
import {
  DOMAIN_OPTIONS,
  SUMMARIES,
  domainLabel,
  summaryApi,
  useProposalAction,
  useSummaryCandidates,
  type ProcurementDomain,
  type ProposalListItemDto,
  type SummaryListItemDto,
} from '../../api/proposals';
import { useAuth } from '../../auth/useAuth';
import { P } from '../../auth/permissions';
import { DataTable } from '../../components/DataTable';
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { PageHeader, useApiError } from '../../components/common';
import { useTableUrlState } from '../../hooks/useUrlState';
import { formatDateTime, formatMoney, formatQuantity } from '../../utils/format';

const FILTERS = ['domain', 'status'] as const;

/** Tổng hợp đề xuất (spec §9.1-8). */
export function SummariesPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS);
  const list = usePagedList<SummaryListItemDto>(SUMMARIES, apiParams);
  const navigate = useNavigate();
  const { can } = useAuth();
  return (
    <>
      <PageHeader
        title="Tổng hợp đề xuất"
        subtitle="Gộp các phiếu đề xuất theo mặt hàng, tổ trưởng duyệt số lượng, Giám đốc ký"
        extra={
          can(P.ProposalAggregate) && (
            <Button
              type="primary"
              icon={<PlusOutlined />}
              onClick={() => navigate('/tong-hop-de-xuat/tao-moi')}
            >
              Lập phiếu tổng hợp
            </Button>
          )
        }
      />
      <DataTable<SummaryListItemDto>
        data={list.data}
        loading={list.isFetching}
        state={state}
        onChange={update}
        searchPlaceholder="Tìm theo số phiếu, kỳ"
        filters={
          <>
            <Select
              allowClear
              placeholder="Mảng"
              style={{ width: 190 }}
              value={state.filters.domain}
              options={DOMAIN_OPTIONS}
              onChange={(v) => update({ filters: { domain: v } })}
            />
            <Select
              allowClear
              placeholder="Trạng thái"
              style={{ width: 190 }}
              value={state.filters.status}
              options={statusOptions('summaryStatus')}
              onChange={(v) => update({ filters: { status: v } })}
            />
          </>
        }
        columns={[
          {
            title: 'Số phiếu',
            dataIndex: 'number',
            render: (v: string, r) => <a onClick={() => navigate(`/tong-hop-de-xuat/${r.id}`)}>{v}</a>,
          },
          { title: 'Mảng', dataIndex: 'domain', render: (v: SummaryListItemDto['domain']) => domainLabel(v) },
          { title: 'Kỳ', dataIndex: 'periodText' },
          { title: 'Người tổng hợp', dataIndex: 'aggregatorName' },
          { title: 'Số phiếu đề xuất', dataIndex: 'proposalCount', align: 'center' },
          { title: 'Mặt hàng', dataIndex: 'lineCount', align: 'center' },
          {
            title: 'Giá trị duyệt',
            dataIndex: 'totalAmount',
            align: 'right',
            render: (v: number) => formatMoney(v),
          },
          {
            title: 'Còn được gọi',
            dataIndex: 'remainingToCallAmount',
            align: 'right',
            render: (v: number) => formatMoney(v),
          },
          { title: 'Ngày lập', dataIndex: 'createdAt', render: (v: string) => formatDateTime(v) },
          {
            title: 'Trạng thái',
            dataIndex: 'status',
            render: (v: string) => <StatusTag domain="summaryStatus" value={v} />,
          },
        ]}
      />
    </>
  );
}

/** Lập phiếu tổng hợp: chọn các phiếu đề xuất đã gửi của mảng. */
export function SummaryCreatePage() {
  const [domain, setDomain] = useState<ProcurementDomain | undefined>();
  const [selected, setSelected] = useState<string[]>([]);
  const [form] = Form.useForm<{ periodText: string; note?: string }>();
  const candidates = useSummaryCandidates(domain);
  const navigate = useNavigate();
  const { message } = App.useApp();
  const onError = useApiError();
  const create = useProposalAction((v: { periodText: string; note?: string }) =>
    summaryApi.create({
      domain: domain!,
      facilityId: null,
      periodText: v.periodText,
      note: v.note ?? null,
      proposalIds: selected,
    }),
  );

  return (
    <>
      <PageHeader
        title="Lập phiếu tổng hợp đề xuất"
        extra={
          <Button
            type="primary"
            icon={<SaveOutlined />}
            disabled={selected.length === 0}
            loading={create.isPending}
            onClick={async () => {
              const v = await form.validateFields();
              try {
                const id = await create.mutateAsync(v);
                message.success('Đã lập phiếu tổng hợp — kiểm tra rồi gửi ký duyệt');
                navigate(`/tong-hop-de-xuat/${id}`);
              } catch (e) {
                onError(e);
              }
            }}
          >
            Tổng hợp ({selected.length} phiếu)
          </Button>
        }
      />
      <Card style={{ marginBottom: 16 }}>
        <Form
          form={form}
          layout="inline"
          initialValues={{ periodText: `Tháng ${dayjs().format('MM/YYYY')}` }}
          style={{ rowGap: 8 }}
        >
          <Form.Item label="Mảng" required>
            <Select
              style={{ width: 220 }}
              value={domain}
              options={DOMAIN_OPTIONS}
              onChange={(v) => {
                setDomain(v);
                setSelected([]);
              }}
            />
          </Form.Item>
          <Form.Item name="periodText" label="Kỳ tổng hợp" rules={[{ required: true, whitespace: true }]}>
            <Input style={{ width: 200 }} />
          </Form.Item>
          <Form.Item name="note" label="Ghi chú">
            <Input style={{ width: 320 }} />
          </Form.Item>
        </Form>
      </Card>
      <Card title="Phiếu đề xuất đã gửi, chưa tổng hợp">
        {!domain ? (
          <Empty description="Chọn mảng để xem các phiếu đề xuất" />
        ) : (
          <>
            {candidates.data?.length === 0 && (
              <Alert type="info" showIcon message="Không có phiếu đề xuất nào chờ tổng hợp." />
            )}
            <Table<ProposalListItemDto>
              rowKey="id"
              size="middle"
              loading={candidates.isFetching}
              dataSource={candidates.data ?? []}
              pagination={false}
              rowSelection={{ selectedRowKeys: selected, onChange: (keys) => setSelected(keys.map(String)) }}
              columns={[
                {
                  title: 'Số phiếu',
                  dataIndex: 'number',
                  render: (v: string, r) => <a onClick={() => navigate(`/de-xuat/${r.id}`)}>{v}</a>,
                },
                { title: 'Khoa/phòng', dataIndex: 'departmentName' },
                { title: 'Kỳ', dataIndex: 'periodText' },
                { title: 'Mặt hàng', dataIndex: 'lineCount', align: 'center' },
                {
                  title: 'Tổng SL',
                  dataIndex: 'totalQuantity',
                  align: 'right',
                  render: (v: number) => formatQuantity(v),
                },
                { title: 'Người lập', dataIndex: 'createdByName' },
                { title: 'Gửi lúc', dataIndex: 'submittedAt', render: (v: string) => formatDateTime(v) },
              ]}
            />
            <Space style={{ marginTop: 8 }}>
              <Checkbox
                checked={!!candidates.data?.length && selected.length === candidates.data.length}
                onChange={(e) =>
                  setSelected(e.target.checked ? (candidates.data ?? []).map((c) => c.id) : [])
                }
              >
                Chọn tất cả
              </Checkbox>
            </Space>
          </>
        )}
      </Card>
    </>
  );
}
