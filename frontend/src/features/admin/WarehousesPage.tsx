import { App, Button, Form, Input, Modal, Select, Switch } from 'antd';
import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { resources, useLookup, usePagedList, useSave, type WarehouseDto } from '../../api/admin';
import { DataTable } from '../../components/DataTable';
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { PageHeader, useApiError } from '../../components/common';
import { useTableUrlState } from '../../hooks/useUrlState';

const FILTERS = ['facilityId', 'isActive'] as const;

export function WarehousesPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS);
  const list = usePagedList<WarehouseDto>(resources.warehouses, apiParams);
  const facilities = useLookup('facilities');
  const departments = useLookup('departments');
  const save = useSave<object>(resources.warehouses);
  const [editing, setEditing] = useState<WarehouseDto | 'new' | null>(null);
  const [form] = Form.useForm();
  const { message } = App.useApp();
  const onError = useApiError();
  const facilityOptions = (facilities.data ?? []).map((f) => ({ value: f.id, label: f.name }));

  const open = (item: WarehouseDto | 'new') => {
    setEditing(item);
    form.setFieldsValue(item === 'new' ? { isActive: true, type: 'Drug' } : item);
  };

  return (
    <>
      <PageHeader title="Kho" subtitle="Kho nhận hàng, ánh xạ với mã kho trên HIS" />
      <DataTable<WarehouseDto>
        exportAs={{ title: 'Kho', url: resources.warehouses, params: apiParams }}
        data={list.data}
        loading={list.isFetching}
        state={state}
        onChange={update}
        searchPlaceholder="Tìm theo mã, tên kho"
        filters={
          <>
            <Select
              allowClear
              placeholder="Cơ sở"
              style={{ width: 200 }}
              value={state.filters.facilityId}
              options={facilityOptions}
              onChange={(v) => update({ filters: { facilityId: v } })}
            />
            <Select
              allowClear
              placeholder="Trạng thái"
              style={{ width: 170 }}
              value={state.filters.isActive}
              options={statusOptions('active')}
              onChange={(v) => update({ filters: { isActive: v } })}
            />
          </>
        }
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => open('new')}>
            Thêm kho
          </Button>
        }
        columns={[
          { title: 'Mã kho', dataIndex: 'code', sorter: true, width: 150 },
          { title: 'Tên kho', dataIndex: 'name', sorter: true },
          {
            title: 'Loại',
            dataIndex: 'type',
            width: 160,
            render: (v: string) => <StatusTag domain="warehouseType" value={v} />,
          },
          { title: 'Cơ sở', dataIndex: 'facilityName', sorter: true },
          { title: 'Mã HIS', dataIndex: 'hisWarehouseCode', width: 110 },
          {
            title: 'Trạng thái',
            dataIndex: 'isActive',
            width: 150,
            render: (v: boolean) => <StatusTag domain="active" value={v} />,
          },
          {
            title: '',
            key: 'actions',
            width: 60,
            render: (_, r) => <Button size="small" icon={<EditOutlined />} onClick={() => open(r)} />,
          },
        ]}
      />
      <Modal
        title={editing === 'new' ? 'Thêm kho' : 'Sửa kho'}
        open={!!editing}
        onCancel={() => setEditing(null)}
        onOk={() => form.submit()}
        confirmLoading={save.isPending}
        okText="Lưu"
        cancelText="Hủy"
        destroyOnHidden
      >
        <Form
          form={form}
          layout="vertical"
          onFinish={async (values) => {
            try {
              await save.mutateAsync({ id: editing === 'new' ? undefined : editing?.id, body: values });
              message.success('Đã lưu kho');
              setEditing(null);
            } catch (e) {
              onError(e);
            }
          }}
        >
          <Form.Item name="code" label="Mã kho" rules={[{ required: true, message: 'Vui lòng nhập mã kho' }]}>
            <Input disabled={editing !== 'new'} maxLength={30} />
          </Form.Item>
          <Form.Item
            name="name"
            label="Tên kho"
            rules={[{ required: true, message: 'Vui lòng nhập tên kho' }]}
          >
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item name="type" label="Loại kho" rules={[{ required: true }]}>
            <Select options={statusOptions('warehouseType')} />
          </Form.Item>
          <Form.Item
            name="facilityId"
            label="Cơ sở"
            rules={[{ required: true, message: 'Vui lòng chọn cơ sở' }]}
          >
            <Select options={facilityOptions} />
          </Form.Item>
          <Form.Item name="hisWarehouseCode" label="Mã kho trên HIS">
            <Input maxLength={50} />
          </Form.Item>
          <Form.Item name="managingDepartmentId" label="Khoa/phòng quản lý (Trưởng khoa ký phiếu nhập)">
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
            />
          </Form.Item>
          <Form.Item name="isActive" label="Đang hoạt động" valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
