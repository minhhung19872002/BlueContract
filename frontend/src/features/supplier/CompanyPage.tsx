import {
  App,
  Button,
  Card,
  Checkbox,
  Col,
  Descriptions,
  Form,
  Input,
  Modal,
  Row,
  Select,
  Table,
  Tag,
  Tooltip,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { useState } from 'react';
import {
  resources,
  useAction,
  useGet,
  useSave,
  type SupplierDto,
  type SupplierStaffDto,
} from '../../api/admin';
import { P } from '../../auth/permissions';
import { useAuth } from '../../auth/useAuth';
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { PageHeader, useApiError } from '../../components/common';
import { formatDate, formatDateTime } from '../../utils/format';
import { USER_NAME_RULE } from '../../utils/validation';

/** Cổng NCC — Tài khoản công ty: company info + staff accounts (managed by SUPPLIER_ADMIN). */
export function CompanyPage() {
  const company = useGet<SupplierDto>(resources.supplierCompany, [resources.supplierCompany]);
  const staff = useGet<SupplierStaffDto[]>(`${resources.supplierCompany}/staff`, [
    resources.supplierCompany,
    'staff',
  ]);
  const create = useSave<object>(`${resources.supplierCompany}/staff`);
  const updateStaff = useAction<{ id: string; status: string; isAdmin: boolean }>(
    resources.supplierCompany,
    ({ id, ...body }) => ({
      url: `${resources.supplierCompany}/staff/${id}`,
      method: 'put',
      body,
    }),
  );
  const { can, user } = useAuth();
  const canManage = can(P.SupplierStaffManage);
  const [open, setOpen] = useState(false);
  const [form] = Form.useForm();
  const { message } = App.useApp();
  const onError = useApiError();
  const c = company.data;

  return (
    <>
      <PageHeader
        title="Tài khoản công ty"
        subtitle="Thông tin nhà cung cấp lưu tại bệnh viện. Liên hệ Tổ Mua sắm nếu cần điều chỉnh."
      />
      <Row gutter={[16, 16]}>
        <Col span={24}>
          <Card loading={company.isLoading} title={c?.name}>
            {c && (
              <Descriptions column={{ xs: 1, md: 2 }} size="small">
                <Descriptions.Item label="Mã số thuế">{c.taxCode}</Descriptions.Item>
                <Descriptions.Item label="Địa chỉ">{c.address}</Descriptions.Item>
                <Descriptions.Item label="Người đại diện">
                  {c.representativeName} — {c.representativeTitle}
                </Descriptions.Item>
                <Descriptions.Item label="Giấy ủy quyền">{c.authorizationDocument}</Descriptions.Item>
                <Descriptions.Item label="Tài khoản ngân hàng">
                  {c.bankAccountNumber} — {c.bankName}
                </Descriptions.Item>
                <Descriptions.Item label="Email nhận thông báo">{c.notificationEmail}</Descriptions.Item>
                <Descriptions.Item label="Điện thoại">{c.phone}</Descriptions.Item>
              </Descriptions>
            )}
          </Card>
        </Col>
        <Col span={24}>
          <Card
            title="Nhân viên"
            extra={
              canManage && (
                <Button
                  type="primary"
                  icon={<PlusOutlined />}
                  onClick={() => {
                    form.resetFields();
                    setOpen(true);
                  }}
                >
                  Thêm tài khoản
                </Button>
              )
            }
          >
            <Table<SupplierStaffDto>
              rowKey="id"
              size="middle"
              loading={staff.isLoading}
              dataSource={staff.data ?? []}
              pagination={false}
              scroll={{ x: 'max-content' }}
              columns={[
                { title: 'Tên đăng nhập', dataIndex: 'userName' },
                { title: 'Họ tên', dataIndex: 'fullName' },
                { title: 'Email', dataIndex: 'email' },
                {
                  title: 'Quyền',
                  dataIndex: 'isAdmin',
                  render: (v: boolean) => (v ? <Tag color="purple">Quản trị</Tag> : <Tag>Nhân viên</Tag>),
                },
                {
                  title: 'Trạng thái',
                  dataIndex: 'status',
                  render: (v: string, r) =>
                    canManage && r.id !== user?.id ? (
                      <Select
                        size="small"
                        value={v}
                        style={{ width: 170 }}
                        options={statusOptions('userStatus').filter((o) => o.value !== 'PendingActivation')}
                        onChange={(status) =>
                          updateStaff
                            .mutateAsync({ id: r.id, status, isAdmin: r.isAdmin })
                            .then(() => message.success('Đã cập nhật'), onError)
                        }
                      />
                    ) : (
                      <StatusTag domain="userStatus" value={v} />
                    ),
                },
                {
                  title: 'Chứng thư số',
                  dataIndex: 'certificateValidTo',
                  render: (v: string | null, r) =>
                    r.certificateSerial ? (
                      <Tooltip title={`Số hiệu ${r.certificateSerial}`}>
                        <span>Hiệu lực đến {formatDate(v)}</span>
                      </Tooltip>
                    ) : (
                      <Tag>Chưa đăng ký</Tag>
                    ),
                },
                {
                  title: 'Đăng nhập gần nhất',
                  dataIndex: 'lastLoginAt',
                  render: (v?: string) => formatDateTime(v),
                },
              ]}
            />
          </Card>
        </Col>
      </Row>
      <Modal
        title="Thêm tài khoản nhân viên"
        open={open}
        onCancel={() => setOpen(false)}
        onOk={() => form.submit()}
        okText="Tạo"
        cancelText="Hủy"
        destroyOnHidden
      >
        <Form
          form={form}
          layout="vertical"
          onFinish={async (v) => {
            try {
              await create.mutateAsync({ body: { ...v, isAdmin: !!v.isAdmin } });
              message.success('Đã tạo tài khoản; mật khẩu tạm đã gửi qua email');
              setOpen(false);
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
          <Form.Item name="isAdmin" valuePropName="checked">
            <Checkbox>Quyền quản trị (quản lý tài khoản nhân viên)</Checkbox>
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
