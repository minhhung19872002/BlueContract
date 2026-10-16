import { Empty, Spin, Typography } from 'antd';
import { useNavigate } from 'react-router-dom';
import { useGet, usePagedList } from '../../api/admin';
import {
  CALL_ORDERS,
  PROPOSALS,
  type CallOrderListItemDto,
  type ProposalListItemDto,
} from '../../api/proposals';
import type { components } from '../../api/schema';
import { StatusTag } from '../../components/StatusTag';
import { Panel } from '../../components/layout';
import { describeAuditDetails } from '../../utils/auditDetails';
import { formatDate, formatDateTime, formatMoney, formatQuantity } from '../../utils/format';
import { ExportableTable } from '../../components/TableExportButton';

type ContractHistoryEntryDto = components['schemas']['ContractHistoryEntryDto'];

/** Proposals with a line on the contract (internal users only). */
function ContractProposals({ contractId }: { contractId: string }) {
  const navigate = useNavigate();
  const proposals = usePagedList<ProposalListItemDto>(PROPOSALS, { contractId, pageSize: 200 });
  return (
    <Panel title={`Phiếu đề xuất (${proposals.data?.total ?? 0})`} flush>
      <ExportableTable<ProposalListItemDto>
        exportTitle="Phiếu đề xuất của hợp đồng"
        rowKey="id"
        size="small"
        loading={proposals.isFetching}
        dataSource={proposals.data?.items ?? []}
        pagination={false}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'Chưa có phiếu đề xuất nào cho hợp đồng này' }}
        columns={[
          {
            title: 'Số phiếu',
            dataIndex: 'number',
            render: (v: string, r) => (
              <a className="mono" onClick={() => navigate(`/de-xuat/${r.id}`)}>
                {v}
              </a>
            ),
          },
          { title: 'Khoa/phòng', dataIndex: 'departmentName' },
          { title: 'Kỳ', dataIndex: 'periodText' },
          { title: 'Số mặt hàng', dataIndex: 'lineCount', align: 'right' },
          {
            title: 'Tổng SL',
            dataIndex: 'totalQuantity',
            align: 'right',
            render: (v: number) => formatQuantity(v),
          },
          { title: 'Tổng hợp', dataIndex: 'summaryNumber', render: (v?: string | null) => v ?? '—' },
          {
            title: 'Trạng thái',
            dataIndex: 'status',
            render: (v: string) => <StatusTag domain="proposalStatus" value={v} />,
          },
        ]}
      />
    </Panel>
  );
}

/** Tab "Đề xuất & gọi hàng" (spec §9.1-5): proposals with a line on the contract and its call orders. */
export function ContractCalls({
  contractId,
  base,
  showProposals,
}: {
  contractId: string;
  base: string;
  showProposals: boolean;
}) {
  const navigate = useNavigate();
  const calls = usePagedList<CallOrderListItemDto>(CALL_ORDERS, { contractId, pageSize: 200 });
  return (
    <div className="contract-tab-stack">
      {showProposals && <ContractProposals contractId={contractId} />}
      <Panel title={`Đơn gọi hàng (${calls.data?.total ?? 0})`} flush>
        <ExportableTable<CallOrderListItemDto>
          exportTitle="Đơn gọi hàng của hợp đồng"
          rowKey="id"
          size="small"
          loading={calls.isFetching}
          dataSource={calls.data?.items ?? []}
          pagination={false}
          scroll={{ x: 'max-content' }}
          locale={{ emptyText: 'Chưa có đơn gọi hàng nào cho hợp đồng này' }}
          columns={[
            {
              title: 'Số đơn',
              dataIndex: 'number',
              render: (v: string, r) => (
                <a className="mono" onClick={() => navigate(`${base}/goi-hang/${r.id}`)}>
                  {v}
                </a>
              ),
            },
            { title: 'Ngày gọi', dataIndex: 'callDate', render: (v: string) => formatDate(v) },
            { title: 'Hạn giao', dataIndex: 'deliveryDeadline', render: (v: string) => formatDate(v) },
            { title: 'Kho nhận', dataIndex: 'warehouseName' },
            { title: 'Tổng hợp', dataIndex: 'summaryNumber' },
            {
              title: 'Giá trị',
              dataIndex: 'totalAmount',
              align: 'right',
              render: (v: number) => formatMoney(v),
            },
            {
              title: 'Trạng thái',
              dataIndex: 'status',
              render: (v: string, r) => (
                <StatusTag
                  domain="callOrderStatus"
                  value={v}
                  suffix={r.isOverdue ? ' · quá hạn' : undefined}
                />
              ),
            },
          ]}
        />
      </Panel>
    </div>
  );
}

/** Tab "Lịch sử": audit trail of the contract, its document and appendices (newest first). */
export function ContractHistory({ contractId }: { contractId: string }) {
  const history = useGet<ContractHistoryEntryDto[]>(`/api/contracts/${contractId}/history`, [
    '/api/contracts',
    contractId,
    'history',
  ]);
  if (history.isLoading) return <Spin style={{ margin: 24 }} />;
  const rows = history.data ?? [];
  if (rows.length === 0) return <Empty description="Chưa có lịch sử" />;
  return (
    <ExportableTable<ContractHistoryEntryDto>
      exportTitle="Lịch sử hợp đồng"
      rowKey={(r, i) => `${r.at}-${i}`}
      size="small"
      dataSource={rows}
      pagination={{ pageSize: 50, hideOnSinglePage: true }}
      scroll={{ x: 'max-content' }}
      columns={[
        { title: 'Thời điểm', dataIndex: 'at', width: 150, render: (v: string) => formatDateTime(v) },
        {
          title: 'Người thực hiện',
          dataIndex: 'userFullName',
          render: (v?: string | null) => v ?? 'Hệ thống',
        },
        {
          title: 'Hành động',
          dataIndex: 'actionLabel',
          render: (v: string, r) => (
            <span>
              <strong>{v}</strong> <Typography.Text type="secondary">· {r.subjectLabel}</Typography.Text>
            </span>
          ),
        },
        {
          title: 'Ghi chú / chi tiết',
          key: 'details',
          render: (_, r) => {
            const text = [r.note, ...describeAuditDetails(r.subject, r.details)].filter(Boolean).join(' · ');
            return text ? <span className="contract-history__details">{text}</span> : '—';
          },
        },
      ]}
    />
  );
}
