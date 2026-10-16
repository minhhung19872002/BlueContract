import { App, Button, Form, InputNumber, Modal, Select } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { usePagedList } from '../../api/admin';
import {
  BIDS,
  bidApi,
  useBidAction,
  useItemCategories,
  type BidDecisionListItemDto,
} from '../../api/procurement';
import { useAuth } from '../../auth/useAuth';
import { P } from '../../auth/permissions';
import { DataTable } from '../../components/DataTable';
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { ExportExcelButton, PageHeader, useApiError } from '../../components/common';
import { useFormDraft } from '../../hooks/useFormDraft';
import { SupplierSelect } from '../../components/inputs';
import { useTableUrlState } from '../../hooks/useUrlState';
import { formatDate, formatMoney } from '../../utils/format';
import { BidForm } from './BidForm';
import { toBidRequest, type BidFormValues } from './bidFormValues';

const FILTERS = ['year', 'supplierId', 'categoryId', 'status'] as const;

/** Quyết định thầu (spec §9.1-3): lọc năm, gói thầu, NCC. */
export function BidDecisionsPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS);
  const list = usePagedList<BidDecisionListItemDto>(BIDS, apiParams);
  const categories = useItemCategories();
  const navigate = useNavigate();
  const { can } = useAuth();
  const { message } = App.useApp();
  const onError = useApiError();
  const [creating, setCreating] = useState(false);
  const [form] = Form.useForm<BidFormValues>();
  const create = useBidAction((v: BidFormValues) => bidApi.create(toBidRequest(v)));
  // Lưu nháp tự động (spec §9.3) of the "Tạo quyết định thầu" form.
  const draft = useFormDraft({ key: 'bid-new', form });

  return (
    <>
      <PageHeader
        title="Quyết định thầu"
        subtitle="Quyết định phê duyệt kết quả lựa chọn nhà thầu, NCC trúng thầu và danh mục hàng hóa"
      />
      <DataTable<BidDecisionListItemDto>
        data={list.data}
        loading={list.isFetching}
        state={state}
        onChange={update}
        searchPlaceholder="Tìm theo số QĐ, gói thầu, trích yếu"
        filters={
          <>
            <InputNumber
              placeholder="Năm"
              min={2000}
              max={2100}
              style={{ width: 100 }}
              value={state.filters.year ? Number(state.filters.year) : null}
              onChange={(v) => update({ filters: { year: v ? String(v) : undefined } })}
            />
            <Select
              allowClear
              placeholder="Hạng mục"
              style={{ width: 170 }}
              value={state.filters.categoryId}
              options={(categories.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
              onChange={(v) => update({ filters: { categoryId: v } })}
            />
            <SupplierSelect
              allowClear
              placeholder="NCC trúng thầu"
              style={{ width: 260 }}
              value={state.filters.supplierId}
              onChange={(v) => update({ filters: { supplierId: v } })}
            />
            <Select
              allowClear
              placeholder="Trạng thái"
              style={{ width: 150 }}
              value={state.filters.status}
              options={statusOptions('bidStatus')}
              onChange={(v) => update({ filters: { status: v } })}
            />
          </>
        }
        actions={
          <>
            <ExportExcelButton url={`${BIDS}/export`} params={apiParams} fileName="quyet-dinh-thau.xlsx" />
            {can(P.BidManage) && (
              <Button
                type="primary"
                icon={<PlusOutlined />}
                onClick={() => {
                  form.resetFields();
                  setCreating(true);
                }}
              >
                Tạo quyết định thầu
              </Button>
            )}
          </>
        }
        columns={[
          {
            title: 'Số quyết định',
            dataIndex: 'number',
            sorter: true,
            render: (v: string, r) => <a onClick={() => navigate(`/quyet-dinh-thau/${r.id}`)}>{v}</a>,
          },
          {
            title: 'Ngày ký',
            dataIndex: 'signedDate',
            sorter: true,
            width: 110,
            render: (v: string) => formatDate(v),
          },
          { title: 'Tên gói thầu', dataIndex: 'packageName', sorter: true },
          { title: 'Hạng mục', dataIndex: 'categoryName', width: 140 },
          { title: 'NCC', dataIndex: 'supplierCount', width: 70, align: 'center' },
          { title: 'Mặt hàng', dataIndex: 'itemCount', width: 90, align: 'center' },
          {
            title: 'Giá trị trúng thầu',
            dataIndex: 'totalValue',
            align: 'right',
            render: (v: number) => formatMoney(v),
          },
          { title: 'Hợp đồng', dataIndex: 'contractCount', width: 90, align: 'center' },
          {
            title: 'Trạng thái',
            dataIndex: 'status',
            width: 140,
            render: (v: string) => <StatusTag domain="bidStatus" value={v} />,
          },
        ]}
      />

      <Modal
        title="Tạo quyết định thầu"
        open={creating}
        onCancel={() => setCreating(false)}
        okText="Tạo"
        confirmLoading={create.isPending}
        width={860}
        onOk={async () => {
          const v = await form.validateFields();
          try {
            const id = await create.mutateAsync(v);
            draft.clear();
            message.success('Đã tạo quyết định thầu — tiếp tục thêm NCC trúng thầu và danh mục');
            setCreating(false);
            navigate(`/quyet-dinh-thau/${id}`);
          } catch (e) {
            onError(e);
          }
        }}
        destroyOnHidden
      >
        {draft.banner}
        <BidForm form={form} onValuesChange={draft.onValuesChange} />
      </Modal>
    </>
  );
}
