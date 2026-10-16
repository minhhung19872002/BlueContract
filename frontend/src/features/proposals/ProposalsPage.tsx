import { Button, Select, Typography } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { usePagedList } from '../../api/admin';
import { DOMAIN_OPTIONS, PROPOSALS, domainLabel, type ProposalListItemDto } from '../../api/proposals';
import { useAuth } from '../../auth/useAuth';
import { P } from '../../auth/permissions';
import { DataTable } from '../../components/DataTable';
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { PageHeader } from '../../components/common';
import { useTableUrlState } from '../../hooks/useUrlState';
import { formatDateTime, formatQuantity } from '../../utils/format';

const FILTERS = ['domain', 'status'] as const;

/** Đề xuất (spec §9.1-7): phiếu đề xuất của khoa mình (người tổng hợp/tổ trưởng thấy theo mảng). */
export function ProposalsPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS);
  const list = usePagedList<ProposalListItemDto>(PROPOSALS, apiParams);
  const navigate = useNavigate();
  const { can } = useAuth();

  return (
    <>
      <PageHeader
        title="Đề xuất"
        subtitle="Phiếu đề xuất gọi hàng của khoa/phòng — số lượng không vượt số còn lại của hợp đồng"
        extra={
          can(P.ProposalCreate) && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/de-xuat/tao-moi')}>
              Lập phiếu đề xuất
            </Button>
          )
        }
      />
      <DataTable<ProposalListItemDto>
        data={list.data}
        loading={list.isFetching}
        state={state}
        onChange={update}
        searchPlaceholder="Tìm theo số phiếu, tên hàng"
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
              style={{ width: 200 }}
              value={state.filters.status}
              options={statusOptions('proposalStatus')}
              onChange={(v) => update({ filters: { status: v } })}
            />
          </>
        }
        columns={[
          {
            title: 'Số phiếu',
            dataIndex: 'number',
            render: (v: string, r) => <a onClick={() => navigate(`/de-xuat/${r.id}`)}>{v}</a>,
          },
          { title: 'Khoa/phòng', dataIndex: 'departmentName' },
          {
            title: 'Mảng',
            dataIndex: 'domain',
            render: (v: ProposalListItemDto['domain']) => domainLabel(v),
          },
          { title: 'Kỳ', dataIndex: 'periodText' },
          { title: 'Mặt hàng', dataIndex: 'lineCount', align: 'center' },
          {
            title: 'Tổng SL đề xuất',
            dataIndex: 'totalQuantity',
            align: 'right',
            render: (v: number) => formatQuantity(v),
          },
          { title: 'Người lập', dataIndex: 'createdByName' },
          { title: 'Ngày lập', dataIndex: 'createdAt', render: (v: string) => formatDateTime(v) },
          {
            title: 'Trạng thái',
            dataIndex: 'status',
            render: (v: string, r) => (
              <>
                <StatusTag domain="proposalStatus" value={v} />
                {r.summaryNumber && <Typography.Text type="secondary">{r.summaryNumber}</Typography.Text>}
              </>
            ),
          },
        ]}
      />
    </>
  );
}
