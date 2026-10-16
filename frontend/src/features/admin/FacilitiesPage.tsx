import { App, Button, Form, Input, Modal, Select, Switch } from 'antd';
import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { resources, usePagedList, useSave, type FacilityDto } from '../../api/admin';
import { DataTable } from '../../components/DataTable';
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { PageHeader, useApiError } from '../../components/common';
import { useTableUrlState } from '../../hooks/useUrlState';

const FILTERS = ['isActive'] as const;

export function FacilitiesPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS);
  const list = usePagedList<FacilityDto>(resources.facilities, apiParams);
  const save = useSave<object>(resources.facilities);
  const [editing, setEditing] = useState<FacilityDto | 'new' | null>(null);
  const [form] = Form.useForm();
  const { message } = App.useApp();
  const onError = useApiError();

  const open = (item: FacilityDto | 'new') => {
    setEditing(item);
    form.setFieldsValue(
      item === 'new' ? { isActive: true, code: '', name: '', address: '', hisFacilityCode: '' } : item,
    );
  };

  return (
    <>
      <PageHeader title="Cơ sở" subtitle="Các cơ sở khám chữa bệnh của bệnh viện" />
      <DataTable<FacilityDto>
        data={list.data}
        loading={list.isFetching}
        state={state}
        onChange={update}
        searchPlaceholder="Tìm theo mã, tên cơ sở"
        filters={
          <Select
            allowClear
            placeholder="Trạng thái"
            style={{ width: 170 }}
            value={state.filters.isActive}
            options={statusOptions('active')}
            onChange={(v) => update({ filters: { isActive: v } })}
          />
        }
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => open('new')}>
            Thêm cơ sở
          </Button>
        }
        columns={[
          { title: 'Mã', dataIndex: 'code', sorter: true, width: 100 },
          { title: 'Tên cơ sở', dataIndex: 'name', sorter: true },
          { title: 'Địa chỉ', dataIndex: 'address' },
          { title: 'Mã HIS', dataIndex: 'hisFacilityCode', width: 110 },
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
        title={editing === 'new' ? 'Thêm cơ sở' : 'Sửa cơ sở'}
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
              message.success('Đã lưu cơ sở');
              setEditing(null);
            } catch (e) {
              onError(e);
            }
          }}
        >
          <Form.Item name="code" label="Mã cơ sở" rules={[{ required: true, message: 'Vui lòng nhập mã' }]}>
            <Input disabled={editing !== 'new'} maxLength={30} />
          </Form.Item>
          <Form.Item name="name" label="Tên cơ sở" rules={[{ required: true, message: 'Vui lòng nhập tên' }]}>
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item name="address" label="Địa chỉ">
            <Input maxLength={500} />
          </Form.Item>
          <Form.Item name="hisFacilityCode" label="Mã cơ sở trên HIS">
            <Input maxLength={50} />
          </Form.Item>
          <Form.Item name="isActive" label="Đang hoạt động" valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
