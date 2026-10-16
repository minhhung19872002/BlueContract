import { App, Button, Form, Input, Modal, Select, Space, Switch } from 'antd';
import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { resources, useLookup, usePagedList, useSave, type DepartmentDto } from '../../api/admin';
import { DataTable } from '../../components/DataTable';
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { ExportExcelButton, HistoryButton, PageHeader, useApiError } from '../../components/common';
import { useTableUrlState } from '../../hooks/useUrlState';

const FILTERS = ['facilityId', 'type', 'isActive'] as const;

export function DepartmentsPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS);
  const list = usePagedList<DepartmentDto>(resources.departments, apiParams);
  const facilities = useLookup('facilities');
  const save = useSave<object>(resources.departments);
  const [editing, setEditing] = useState<DepartmentDto | 'new' | null>(null);
  const [form] = Form.useForm();
  const { message } = App.useApp();
  const onError = useApiError();
  const facilityOptions = (facilities.data ?? []).map((f) => ({ value: f.id, label: f.name }));

  const open = (item: DepartmentDto | 'new') => {
    setEditing(item);
    form.setFieldsValue(item === 'new' ? { isActive: true, type: 'Clinical' } : item);
  };

  return (
    <>
      <PageHeader title="Khoa/phòng" subtitle="Danh mục khoa, phòng — mọi thay đổi được lưu lịch sử" />
      <DataTable<DepartmentDto>
        data={list.data}
        loading={list.isFetching}
        state={state}
        onChange={update}
        searchPlaceholder="Tìm theo mã, tên khoa/phòng"
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
              placeholder="Loại"
              style={{ width: 170 }}
              value={state.filters.type}
              options={statusOptions('departmentType')}
              onChange={(v) => update({ filters: { type: v } })}
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
          <>
            <ExportExcelButton
              url={`${resources.departments}/export`}
              params={apiParams}
              fileName="khoa-phong.xlsx"
            />
            <Button type="primary" icon={<PlusOutlined />} onClick={() => open('new')}>
              Thêm khoa/phòng
            </Button>
          </>
        }
        columns={[
          { title: 'Mã', dataIndex: 'code', sorter: true, width: 110 },
          { title: 'Tên khoa/phòng', dataIndex: 'name', sorter: true },
          {
            title: 'Loại',
            dataIndex: 'type',
            sorter: true,
            width: 150,
            render: (v: string) => <StatusTag domain="departmentType" value={v} />,
          },
          { title: 'Cơ sở', dataIndex: 'facilityName', sorter: true },
          { title: 'Mã HIS', dataIndex: 'hisDepartmentCode', width: 120 },
          {
            title: 'Trạng thái',
            dataIndex: 'isActive',
            width: 150,
            render: (v: boolean) => <StatusTag domain="active" value={v} />,
          },
          {
            title: '',
            key: 'actions',
            width: 140,
            render: (_, r) => (
              <Space>
                <Button size="small" icon={<EditOutlined />} onClick={() => open(r)} />
                <HistoryButton url={`${resources.departments}/${r.id}/history`} title={r.name} />
              </Space>
            ),
          },
        ]}
      />
      <Modal
        title={editing === 'new' ? 'Thêm khoa/phòng' : 'Sửa khoa/phòng'}
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
              message.success('Đã lưu khoa/phòng');
              setEditing(null);
            } catch (e) {
              onError(e);
            }
          }}
        >
          <Form.Item name="code" label="Mã" rules={[{ required: true, message: 'Vui lòng nhập mã' }]}>
            <Input disabled={editing !== 'new'} maxLength={30} />
          </Form.Item>
          <Form.Item
            name="name"
            label="Tên khoa/phòng"
            rules={[{ required: true, message: 'Vui lòng nhập tên' }]}
          >
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item name="type" label="Loại" rules={[{ required: true }]}>
            <Select options={statusOptions('departmentType')} />
          </Form.Item>
          <Form.Item
            name="facilityId"
            label="Cơ sở"
            rules={[{ required: true, message: 'Vui lòng chọn cơ sở' }]}
          >
            <Select options={facilityOptions} />
          </Form.Item>
          <Form.Item name="hisDepartmentCode" label="Mã khoa trên HIS">
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
