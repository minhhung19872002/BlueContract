import {
  App,
  Button,
  Checkbox,
  Col,
  Divider,
  Form,
  Input,
  Modal,
  Row,
  Select,
  Space,
  Switch,
  Tooltip,
} from 'antd';
import { EditOutlined, PlusOutlined, UserAddOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { resources, useAction, usePagedList, useSave, type SupplierDto } from '../../api/admin';
import { DataTable } from '../../components/DataTable';
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { ExportExcelButton, HistoryButton, PageHeader, useApiError } from '../../components/common';
import { useTableUrlState } from '../../hooks/useUrlState';
import { TAX_CODE_RULE, USER_NAME_RULE } from '../../utils/validation';

const FILTERS = ['isActive'] as const;

export function SuppliersPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS);
  const list = usePagedList<SupplierDto>(resources.suppliers, apiParams);
  const save = useSave<object>(resources.suppliers);
  const [editing, setEditing] = useState<SupplierDto | 'new' | null>(null);
  const [accountFor, setAccountFor] = useState<SupplierDto | null>(null);
  const [form] = Form.useForm();
  const [accountForm] = Form.useForm();
  const { message } = App.useApp();
  const onError = useApiError();
  const createAccount = useAction<{ id: string; asAdmin: boolean; body: object }>(
    resources.suppliers,
    ({ id, asAdmin, body }) => ({
      url: `${resources.suppliers}/${id}/accounts?asAdmin=${asAdmin}`,
      body,
    }),
  );
  const withAccount = Form.useWatch('withAccount', form) as boolean | undefined;

  const open = (item: SupplierDto | 'new') => {
    setEditing(item);
    form.resetFields();
    form.setFieldsValue(item === 'new' ? { isActive: true, withAccount: true } : item);
  };

  return (
    <>
      <PageHeader title="Nhà cung cấp" subtitle="Mã số thuế là khóa liên kết với HIS (Số QĐ + MST)" />
      <DataTable<SupplierDto>
        data={list.data}
        loading={list.isFetching}
        state={state}
        onChange={update}
        searchPlaceholder="Tìm theo MST, tên nhà cung cấp"
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
          <>
            <ExportExcelButton
              url={`${resources.suppliers}/export`}
              params={apiParams}
              fileName="nha-cung-cap.xlsx"
            />
            <Button type="primary" icon={<PlusOutlined />} onClick={() => open('new')}>
              Thêm nhà cung cấp
            </Button>
          </>
        }
        columns={[
          { title: 'Mã số thuế', dataIndex: 'taxCode', sorter: true, width: 140 },
          { title: 'Tên nhà cung cấp', dataIndex: 'name', sorter: true },
          { title: 'Người đại diện', dataIndex: 'representativeName' },
          { title: 'Email thông báo', dataIndex: 'notificationEmail' },
          { title: 'Tài khoản', dataIndex: 'accountCount', width: 90, align: 'center' },
          {
            title: 'Trạng thái',
            dataIndex: 'isActive',
            width: 150,
            render: (v: boolean) => <StatusTag domain="active" value={v} />,
          },
          {
            title: '',
            key: 'actions',
            width: 170,
            render: (_, r) => (
              <Space>
                <Button size="small" icon={<EditOutlined />} onClick={() => open(r)} />
                <Tooltip title="Cấp tài khoản">
                  <Button
                    size="small"
                    icon={<UserAddOutlined />}
                    onClick={() => {
                      setAccountFor(r);
                      accountForm.resetFields();
                    }}
                  />
                </Tooltip>
                <HistoryButton url={`${resources.suppliers}/${r.id}/history`} title={r.name} />
              </Space>
            ),
          },
        ]}
      />

      <Modal
        title={editing === 'new' ? 'Thêm nhà cung cấp' : 'Sửa nhà cung cấp'}
        open={!!editing}
        onCancel={() => setEditing(null)}
        onOk={() => form.submit()}
        confirmLoading={save.isPending}
        okText="Lưu"
        cancelText="Hủy"
        width={760}
        destroyOnHidden
      >
        <Form
          form={form}
          layout="vertical"
          onFinish={async ({ withAccount: wa, adminUserName, adminFullName, adminEmail, ...values }) => {
            const body = {
              ...values,
              adminAccount:
                editing === 'new' && wa
                  ? { userName: adminUserName, fullName: adminFullName, email: adminEmail }
                  : null,
            };
            try {
              await save.mutateAsync({ id: editing === 'new' ? undefined : editing?.id, body });
              message.success(
                body.adminAccount
                  ? 'Đã lưu nhà cung cấp và gửi thông tin tài khoản qua email'
                  : 'Đã lưu nhà cung cấp',
              );
              setEditing(null);
            } catch (e) {
              onError(e);
            }
          }}
        >
          <Row gutter={16}>
            <Col xs={24} md={8}>
              <Form.Item
                name="taxCode"
                label="Mã số thuế"
                rules={[{ required: true, message: 'Vui lòng nhập MST' }, TAX_CODE_RULE]}
              >
                <Input disabled={editing !== 'new'} />
              </Form.Item>
            </Col>
            <Col xs={24} md={16}>
              <Form.Item
                name="name"
                label="Tên nhà cung cấp"
                rules={[{ required: true, message: 'Vui lòng nhập tên' }]}
              >
                <Input maxLength={300} />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="shortName" label="Tên viết tắt">
                <Input maxLength={100} />
              </Form.Item>
            </Col>
            <Col xs={24} md={16}>
              <Form.Item name="address" label="Địa chỉ">
                <Input maxLength={500} />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="representativeName" label="Người đại diện">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="representativeTitle" label="Chức vụ">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="authorizationDocument" label="Giấy ủy quyền (số, ngày)">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="bankAccountNumber" label="Số tài khoản">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={16}>
              <Form.Item name="bankName" label="Ngân hàng">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item
                name="notificationEmail"
                label="Email nhận thông báo"
                rules={[{ type: 'email', message: 'Email không hợp lệ' }]}
              >
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={6}>
              <Form.Item name="phone" label="Điện thoại">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={6}>
              <Form.Item name="isActive" label="Đang hoạt động" valuePropName="checked">
                <Switch />
              </Form.Item>
            </Col>
          </Row>
          {editing === 'new' && (
            <>
              <Divider style={{ margin: '4px 0 12px' }} />
              <Form.Item name="withAccount" valuePropName="checked">
                <Checkbox>Cấp ngay tài khoản quản trị cho nhà cung cấp (mật khẩu tạm gửi qua email)</Checkbox>
              </Form.Item>
              {withAccount && (
                <Row gutter={16}>
                  <Col xs={24} md={8}>
                    <Form.Item
                      name="adminUserName"
                      label="Tên đăng nhập"
                      rules={[{ required: true, message: 'Bắt buộc' }, USER_NAME_RULE]}
                    >
                      <Input />
                    </Form.Item>
                  </Col>
                  <Col xs={24} md={8}>
                    <Form.Item
                      name="adminFullName"
                      label="Họ tên"
                      rules={[{ required: true, message: 'Bắt buộc' }]}
                    >
                      <Input />
                    </Form.Item>
                  </Col>
                  <Col xs={24} md={8}>
                    <Form.Item
                      name="adminEmail"
                      label="Email"
                      rules={[{ required: true, type: 'email', message: 'Email không hợp lệ' }]}
                    >
                      <Input />
                    </Form.Item>
                  </Col>
                </Row>
              )}
            </>
          )}
        </Form>
      </Modal>

      <Modal
        title={`Cấp tài khoản — ${accountFor?.name ?? ''}`}
        open={!!accountFor}
        onCancel={() => setAccountFor(null)}
        onOk={() => accountForm.submit()}
        okText="Cấp tài khoản"
        cancelText="Hủy"
        destroyOnHidden
      >
        <Form
          form={accountForm}
          layout="vertical"
          onFinish={async ({ asAdmin, ...body }) => {
            try {
              await createAccount.mutateAsync({ id: accountFor!.id, asAdmin: !!asAdmin, body });
              message.success('Đã cấp tài khoản và gửi thông tin đăng nhập qua email');
              setAccountFor(null);
            } catch (e) {
              onError(e);
            }
          }}
        >
          <Form.Item
            name="userName"
            label="Tên đăng nhập"
            rules={[{ required: true, message: 'Bắt buộc' }, USER_NAME_RULE]}
          >
            <Input />
          </Form.Item>
          <Form.Item name="fullName" label="Họ tên" rules={[{ required: true, message: 'Bắt buộc' }]}>
            <Input />
          </Form.Item>
          <Form.Item
            name="email"
            label="Email"
            rules={[{ required: true, type: 'email', message: 'Email không hợp lệ' }]}
          >
            <Input />
          </Form.Item>
          <Form.Item name="asAdmin" valuePropName="checked">
            <Checkbox>Quyền quản trị nhà cung cấp (quản lý tài khoản nhân viên)</Checkbox>
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
