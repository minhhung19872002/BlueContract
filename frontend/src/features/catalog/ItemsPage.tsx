import {
  App,
  Button,
  Col,
  Dropdown,
  Form,
  Input,
  Modal,
  Row,
  Select,
  Space,
  Switch,
  Tabs,
  Typography,
} from 'antd';
import { DownloadOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { usePagedList, useSave } from '../../api/admin';
import {
  ITEMS,
  downloadImportTemplate,
  useItemCategories,
  type ItemDto,
  type SaveItemRequest,
} from '../../api/procurement';
import { useAuth } from '../../auth/useAuth';
import { P } from '../../auth/permissions';
import { DataTable } from '../../components/DataTable';
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { ExportExcelButton, PageHeader, useApiError } from '../../components/common';
import { SupplierSelect } from '../../components/inputs';
import { useTableUrlState } from '../../hooks/useUrlState';
import { formatMoney } from '../../utils/format';

const FILTERS = ['categoryId', 'source', 'supplierId'] as const;

/** Danh mục hàng hóa (spec §9.1-4): tabs by category, search, source (HIS / Import / Nhập tay). */
export function ItemsPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS);
  const list = usePagedList<ItemDto>(ITEMS, apiParams);
  const categories = useItemCategories();
  const save = useSave<SaveItemRequest>(ITEMS);
  const { can } = useAuth();
  const { message } = App.useApp();
  const onError = useApiError();
  const [editing, setEditing] = useState<ItemDto | 'new' | null>(null);
  const [form] = Form.useForm<SaveItemRequest>();

  const open = (item: ItemDto | 'new') => {
    setEditing(item);
    form.resetFields();
    form.setFieldsValue(
      item === 'new'
        ? { isActive: true, categoryId: state.filters.categoryId }
        : (item as unknown as SaveItemRequest),
    );
  };

  return (
    <>
      <PageHeader
        title="Danh mục hàng hóa"
        subtitle="Hàng hóa, dịch vụ theo hạng mục — nguồn từ HIS, import Excel hoặc nhập tay"
        extra={
          <Dropdown
            menu={{
              items: [
                { key: 'Medical', label: 'Biểu mẫu import thuốc, VTYT, hóa chất, sinh phẩm (.xlsx)' },
                { key: 'General', label: 'Biểu mẫu import hàng hóa, tài sản, dịch vụ (.xlsx)' },
              ],
              onClick: ({ key }) => downloadImportTemplate(key as 'Medical' | 'General').catch(onError),
            }}
          >
            <Button icon={<DownloadOutlined />}>Biểu mẫu import</Button>
          </Dropdown>
        }
      />
      <Tabs
        activeKey={state.filters.categoryId ?? 'all'}
        onChange={(k) => update({ filters: { categoryId: k === 'all' ? undefined : k } })}
        items={[
          { key: 'all', label: 'Tất cả' },
          ...(categories.data ?? []).map((c) => ({ key: c.id, label: c.name })),
        ]}
      />
      <DataTable<ItemDto>
        data={list.data}
        loading={list.isFetching}
        state={state}
        onChange={update}
        searchPlaceholder="Tìm theo mã, tên, hoạt chất, mã key HIS"
        filters={
          <>
            <Select
              allowClear
              placeholder="Nguồn"
              style={{ width: 150 }}
              value={state.filters.source}
              options={statusOptions('itemSource')}
              onChange={(v) => update({ filters: { source: v } })}
            />
            <SupplierSelect
              allowClear
              style={{ width: 280 }}
              value={state.filters.supplierId}
              onChange={(v) => update({ filters: { supplierId: v } })}
            />
          </>
        }
        actions={
          <>
            <ExportExcelButton url={`${ITEMS}/export`} params={apiParams} fileName="danh-muc-hang-hoa.xlsx" />
            {can(P.ItemManage) && (
              <Button type="primary" icon={<PlusOutlined />} onClick={() => open('new')}>
                Thêm hàng hóa
              </Button>
            )}
          </>
        }
        columns={[
          { title: 'Mã hàng', dataIndex: 'code', sorter: true, width: 110 },
          {
            title: 'Tên hàng',
            dataIndex: 'name',
            sorter: true,
            render: (v: string, r) => (
              <Space direction="vertical" size={0}>
                <Typography.Text strong>{v}</Typography.Text>
                {r.activeIngredient && (
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {[r.activeIngredient, r.strength].filter(Boolean).join(' ')}
                  </Typography.Text>
                )}
              </Space>
            ),
          },
          { title: 'Quy cách', dataIndex: 'specification' },
          { title: 'ĐVT', dataIndex: 'unit', width: 70 },
          { title: 'Hạng mục', dataIndex: 'categoryName', width: 130 },
          { title: 'Nhà cung cấp', dataIndex: 'supplierName' },
          { title: 'Số QĐ thầu', dataIndex: 'bidDecisionNumber', width: 130 },
          {
            title: 'Giá trúng thầu',
            dataIndex: 'winningUnitPrice',
            sorter: true,
            align: 'right',
            render: (v: number | null) => formatMoney(v),
          },
          { title: 'Mã key HIS', dataIndex: 'hisKey', width: 110 },
          {
            title: 'Nguồn',
            dataIndex: 'source',
            width: 120,
            render: (v: string) => <StatusTag domain="itemSource" value={v} />,
          },
          ...(can(P.ItemManage)
            ? [
                {
                  title: '',
                  key: 'actions',
                  width: 60,
                  render: (_: unknown, r: ItemDto) => (
                    <Button size="small" icon={<EditOutlined />} onClick={() => open(r)} />
                  ),
                },
              ]
            : []),
        ]}
      />

      <Modal
        title={editing === 'new' ? 'Thêm hàng hóa (nhập tay)' : 'Sửa hàng hóa'}
        open={!!editing}
        onCancel={() => setEditing(null)}
        okText="Lưu"
        confirmLoading={save.isPending}
        width={760}
        onOk={async () => {
          const body = await form.validateFields();
          try {
            await save.mutateAsync({ id: editing === 'new' ? undefined : (editing as ItemDto).id, body });
            message.success('Đã lưu hàng hóa');
            setEditing(null);
          } catch (e) {
            onError(e);
          }
        }}
        destroyOnHidden
      >
        <Form form={form} layout="vertical">
          <Row gutter={12}>
            <Col xs={24} md={8}>
              <Form.Item name="code" label="Mã hàng" rules={[{ required: true, whitespace: true }]}>
                <Input disabled={editing !== 'new'} />
              </Form.Item>
            </Col>
            <Col xs={24} md={16}>
              <Form.Item
                name="name"
                label="Tên hàng hóa / dịch vụ"
                rules={[{ required: true, whitespace: true }]}
              >
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="categoryId" label="Hạng mục" rules={[{ required: true }]}>
                <Select options={(categories.data ?? []).map((c) => ({ value: c.id, label: c.name }))} />
              </Form.Item>
            </Col>
            <Col xs={12} md={8}>
              <Form.Item name="unit" label="Đơn vị tính" rules={[{ required: true, whitespace: true }]}>
                <Input />
              </Form.Item>
            </Col>
            <Col xs={12} md={8}>
              <Form.Item name="hisKey" label="Mã key HIS">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item name="activeIngredient" label="Hoạt chất / thông số kỹ thuật">
                <Input.TextArea rows={2} />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item name="specification" label="Quy cách">
                <Input.TextArea rows={2} />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="strength" label="Nồng độ, hàm lượng">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="manufacturer" label="Hãng sản xuất">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="country" label="Nước sản xuất">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="registrationNumber" label="Số đăng ký / GPNK / model">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="bidGroup" label="Nhóm thầu">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="supplierId" label="Nhà cung cấp">
                <SupplierSelect allowClear disabled={editing !== 'new'} />
              </Form.Item>
            </Col>
            <Col span={24}>
              <Form.Item name="isActive" label="Đang sử dụng" valuePropName="checked">
                <Switch />
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Modal>
    </>
  );
}
