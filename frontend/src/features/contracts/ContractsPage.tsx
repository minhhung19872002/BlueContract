import { Button, Checkbox, Progress, Select, Space, Typography } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { usePagedList } from '../../api/admin';
import { CONTRACTS, useItemCategories, type ContractListItemDto } from '../../api/procurement';
import { useAuth } from '../../auth/useAuth';
import { P } from '../../auth/permissions';
import { DataTable } from '../../components/DataTable';
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { ExportExcelButton, PageHeader } from '../../components/common';
import { SupplierSelect } from '../../components/inputs';
import { useTableUrlState } from '../../hooks/useUrlState';
import { formatDate, formatMoney } from '../../utils/format';
import dayjs from '../../i18n/dayjs';

const FILTERS = [
  'supplierId',
  'categoryId',
  'status',
  'procurementType',
  'effectiveOnly',
  'expiringSoon',
] as const;

/** Hợp đồng (spec §9.1-5, §9.2-2): search by supplier, category, name, number, status, still effective. */
export function ContractsPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS);
  const list = usePagedList<ContractListItemDto>(CONTRACTS, apiParams);
  const categories = useItemCategories();
  const navigate = useNavigate();
  const { can, isSupplier } = useAuth();
  const base = isSupplier ? '/ncc/hop-dong' : '/hop-dong';

  return (
    <>
      <PageHeader
        title="Hợp đồng"
        subtitle={isSupplier ? 'Hợp đồng của Quý công ty với Bệnh viện' : 'Hợp đồng trong thầu và ngoài thầu'}
        extra={
          can(P.ContractCreate) && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/hop-dong/tao-moi')}>
              Tạo hợp đồng
            </Button>
          )
        }
      />
      <DataTable<ContractListItemDto>
        data={list.data}
        loading={list.isFetching}
        state={state}
        onChange={update}
        searchPlaceholder="Tìm theo số, tên hợp đồng, NCC, MST"
        filters={
          <>
            {!isSupplier && (
              <SupplierSelect
                allowClear
                style={{ width: 260 }}
                value={state.filters.supplierId}
                onChange={(v) => update({ filters: { supplierId: v } })}
              />
            )}
            <Select
              allowClear
              placeholder="Hạng mục"
              style={{ width: 160 }}
              value={state.filters.categoryId}
              options={(categories.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
              onChange={(v) => update({ filters: { categoryId: v } })}
            />
            <Select
              allowClear
              placeholder="Trạng thái"
              style={{ width: 190 }}
              value={state.filters.status}
              options={statusOptions('contractStatus')}
              onChange={(v) => update({ filters: { status: v } })}
            />
            <Select
              allowClear
              placeholder="Hình thức"
              style={{ width: 130 }}
              value={state.filters.procurementType}
              options={statusOptions('procurementType')}
              onChange={(v) => update({ filters: { procurementType: v } })}
            />
            <Checkbox
              checked={state.filters.effectiveOnly === 'true'}
              onChange={(e) => update({ filters: { effectiveOnly: e.target.checked ? 'true' : undefined } })}
            >
              Còn hiệu lực
            </Checkbox>
            <Checkbox
              checked={state.filters.expiringSoon === 'true'}
              onChange={(e) => update({ filters: { expiringSoon: e.target.checked ? 'true' : undefined } })}
            >
              Sắp hết hạn (≤ 30 ngày)
            </Checkbox>
          </>
        }
        actions={
          <ExportExcelButton url={`${CONTRACTS}/export`} params={apiParams} fileName="hop-dong.xlsx" />
        }
        columns={[
          {
            title: 'Số hợp đồng',
            dataIndex: 'number',
            sorter: true,
            render: (v: string, r) => (
              <Space direction="vertical" size={0}>
                <a onClick={() => navigate(`${base}/${r.id}`)}>{v}</a>
                <StatusTag domain="procurementType" value={r.procurementType} />
              </Space>
            ),
          },
          {
            title: 'Tên hợp đồng',
            dataIndex: 'name',
            sorter: true,
            render: (v: string, r) => (
              <Space direction="vertical" size={0}>
                <span>{v}</span>
                {r.bidDecisionNumber && (
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    QĐ {r.bidDecisionNumber}
                  </Typography.Text>
                )}
              </Space>
            ),
          },
          ...(isSupplier ? [] : [{ title: 'Nhà cung cấp', dataIndex: 'supplierName' }]),
          { title: 'Hạng mục', dataIndex: 'categoryName', width: 130 },
          {
            title: 'Giá trị',
            dataIndex: 'totalAmount',
            sorter: true,
            align: 'right' as const,
            render: (v: number) => formatMoney(v),
          },
          {
            title: 'Hiệu lực',
            dataIndex: 'expiryDate',
            sorter: true,
            render: (_: unknown, r: ContractListItemDto) => {
              if (!r.effectiveDate && !r.expiryDate) return '—';
              const soon =
                r.status === 'Effective' && r.expiryDate && dayjs(r.expiryDate).diff(dayjs(), 'day') <= 30;
              return (
                <Typography.Text type={soon ? 'danger' : undefined}>
                  {formatDate(r.effectiveDate)} – {formatDate(r.expiryDate)}
                </Typography.Text>
              );
            },
          },
          {
            title: 'Đã nhập kho',
            dataIndex: 'receivedPercent',
            width: 130,
            render: (v: number) => <Progress percent={Number(v)} size="small" />,
          },
          {
            title: 'Trạng thái',
            dataIndex: 'status',
            render: (v: string) => <StatusTag domain="contractStatus" value={v} />,
          },
        ]}
      />
    </>
  );
}
