import {
  Alert,
  App,
  Button,
  Col,
  Drawer,
  Form,
  Input,
  Popconfirm,
  Row,
  Select,
  Space,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import { EditOutlined, KeyOutlined, LockOutlined, PlusOutlined, UnlockOutlined } from '@ant-design/icons';
import { useEffect, useState } from 'react';
import {
  resources,
  useAction,
  useGet,
  useLookup,
  usePagedList,
  useSave,
  type UserDetailDto,
  type UserListItemDto,
} from '../../api/admin';
import { DataTable } from '../../components/DataTable';
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { ExportExcelButton, HistoryButton, PageHeader, useApiError } from '../../components/common';
import { useTableUrlState } from '../../hooks/useUrlState';
import { formatDateTime } from '../../utils/format';
import { PASSWORD_RULE, USER_NAME_RULE } from '../../utils/validation';

const FILTERS = ['userType', 'supplierId', 'departmentId', 'roleCode', 'status'] as const;

const DOMAIN_OPTIONS = [
  { value: 'Drug', label: 'Thuốc' },
  { value: 'MedicalSupply', label: 'Vật tư y tế' },
  { value: 'Chemical', label: 'Hóa chất, sinh phẩm' },
  { value: 'Equipment', label: 'Tài sản, trang thiết bị' },
  { value: 'Administration', label: 'Hành chính, dịch vụ' },
];

type ScopeForm = { facilityScopes?: string[]; departmentScopes?: string[]; domainScopes?: string[] };

function toScopes(v: ScopeForm) {
  return [
    ...(v.facilityScopes ?? []).map((value) => ({ type: 'Facility' as const, value })),
    ...(v.departmentScopes ?? []).map((value) => ({ type: 'Department' as const, value })),
    ...(v.domainScopes ?? []).map((value) => ({ type: 'Domain' as const, value })),
  ];
}

export function UsersPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS);
  const list = usePagedList<UserListItemDto>(resources.users, apiParams);
  const roles = useLookup('roles');
  const departments = useLookup('departments');
  const facilities = useLookup('facilities');
  const suppliers = useLookup('suppliers');
  const save = useSave<object, UserDetailDto>(resources.users);
  const unlock = useAction<string>(resources.users, (id) => ({ url: `${resources.users}/${id}/unlock` }));
  const reset = useAction<string, { temporaryPassword: string; emailSent: boolean }>(
    resources.users,
    (id) => ({
      url: `${resources.users}/${id}/reset-password`,
    }),
  );
  const [editingId, setEditingId] = useState<string | 'new' | null>(null);
  const detail = useGet<UserDetailDto>(
    editingId && editingId !== 'new' ? `${resources.users}/${editingId}` : null,
  );
  const [form] = Form.useForm();
  const { message, modal } = App.useApp();
  const onError = useApiError();
  const userType = Form.useWatch('userType', form) as string | undefined;

  const roleOptions = (roles.data ?? [])
    .filter((r) =>
      userType === 'Supplier' ? r.code.startsWith('SUPPLIER_') : !r.code.startsWith('SUPPLIER_'),
    )
    .map((r) => ({ value: r.id, label: `${r.name} (${r.code})` }));
  const departmentOptions = (departments.data ?? []).map((d) => ({ value: d.id, label: d.name }));
  const facilityOptions = (facilities.data ?? []).map((f) => ({ value: f.id, label: f.name }));

  const openNew = () => {
    setEditingId('new');
    form.resetFields();
    form.setFieldsValue({ userType: 'Internal', status: 'Active', roleIds: [] });
  };

  const openEdit = (id: string) => {
    setEditingId(id);
    form.resetFields();
  };

  // Fill the form once the detail arrives.
  const loaded = detail.data;
  useEffect(() => {
    if (!loaded || editingId !== loaded.id) return;
    const scopes = loaded.dataScopes ?? [];
    form.setFieldsValue({
      ...loaded,
      facilityScopes: scopes.filter((s) => s.type === 'Facility').map((s) => s.value),
      departmentScopes: scopes.filter((s) => s.type === 'Department').map((s) => s.value),
      domainScopes: scopes.filter((s) => s.type === 'Domain').map((s) => s.value),
    });
  }, [loaded, editingId, form]);

  const submit = async (v: Record<string, unknown> & ScopeForm) => {
    const dataScopes = toScopes(v);
    try {
      if (editingId === 'new') {
        await save.mutateAsync({
          body: {
            userName: v.userName,
            fullName: v.fullName,
            email: v.email,
            phone: v.phone,
            jobTitle: v.jobTitle,
            userType: v.userType,
            departmentId: v.departmentId,
            facilityId: v.facilityId,
            supplierId: v.supplierId,
            roleIds: v.roleIds,
            dataScopes,
            password: v.password || null,
          },
        });
        message.success(v.password ? 'Đã tạo người dùng' : 'Đã tạo người dùng và gửi mật khẩu tạm qua email');
      } else {
        await save.mutateAsync({
          id: editingId!,
          body: {
            fullName: v.fullName,
            email: v.email,
            phone: v.phone,
            jobTitle: v.jobTitle,
            departmentId: v.departmentId,
            facilityId: v.facilityId,
            status: v.status,
            roleIds: v.roleIds,
            dataScopes,
            certificateSerial: v.certificateSerial,
            certificateIssuer: v.certificateIssuer,
            certificateValidTo: v.certificateValidTo || null,
          },
        });
        message.success('Đã cập nhật người dùng');
      }
      setEditingId(null);
    } catch (e) {
      onError(e);
    }
  };

  return (
    <>
      <PageHeader title="Người dùng" subtitle="Tài khoản nội bộ và tài khoản nhà cung cấp" />
      <DataTable<UserListItemDto>
        data={list.data}
        loading={list.isFetching}
        state={state}
        onChange={update}
        searchPlaceholder="Tìm theo tên đăng nhập, họ tên, email"
        filters={
          <>
            <Select
              allowClear
              placeholder="Loại tài khoản"
              style={{ width: 160 }}
              value={state.filters.userType}
              options={statusOptions('userType')}
              onChange={(v) => update({ filters: { userType: v } })}
            />
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Vai trò"
              style={{ width: 200 }}
              value={state.filters.roleCode}
              options={(roles.data ?? []).map((r) => ({ value: r.code, label: r.name }))}
              onChange={(v) => update({ filters: { roleCode: v } })}
            />
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Khoa/phòng"
              style={{ width: 200 }}
              value={state.filters.departmentId}
              options={departmentOptions}
              onChange={(v) => update({ filters: { departmentId: v } })}
            />
            <Select
              allowClear
              placeholder="Trạng thái"
              style={{ width: 160 }}
              value={state.filters.status}
              options={statusOptions('userStatus')}
              onChange={(v) => update({ filters: { status: v } })}
            />
          </>
        }
        actions={
          <>
            <ExportExcelButton
              url={`${resources.users}/export`}
              params={apiParams}
              fileName="nguoi-dung.xlsx"
            />
            <Button type="primary" icon={<PlusOutlined />} onClick={openNew}>
              Thêm người dùng
            </Button>
          </>
        }
        columns={[
          { title: 'Tên đăng nhập', dataIndex: 'userName', sorter: true, width: 150 },
          {
            title: 'Họ tên',
            dataIndex: 'fullName',
            sorter: true,
            render: (v: string, r) => (
              <Space direction="vertical" size={0}>
                <span>{v}</span>
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  {r.jobTitle}
                </Typography.Text>
              </Space>
            ),
          },
          {
            title: 'Loại',
            dataIndex: 'userType',
            width: 130,
            render: (v: string) => <StatusTag domain="userType" value={v} />,
          },
          { title: 'Đơn vị', key: 'unit', render: (_, r) => r.supplierName ?? r.departmentName },
          {
            title: 'Vai trò',
            dataIndex: 'roleCodes',
            render: (v: string[]) => v.map((c) => <Tag key={c}>{c}</Tag>),
          },
          {
            title: 'Trạng thái',
            dataIndex: 'status',
            width: 170,
            render: (v: string, r) => (
              <Space size={4} wrap>
                <StatusTag domain="userStatus" value={v} />
                {r.isLockedOut && (
                  <Tag color="red" icon={<LockOutlined />}>
                    Đang khóa
                  </Tag>
                )}
                {r.twoFactorEnabled && <Tag color="green">2FA</Tag>}
              </Space>
            ),
          },
          {
            title: 'Đăng nhập gần nhất',
            dataIndex: 'lastLoginAt',
            sorter: true,
            width: 160,
            render: (v?: string) => formatDateTime(v),
          },
          {
            title: '',
            key: 'actions',
            width: 190,
            render: (_, r) => (
              <Space>
                <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(r.id)} />
                {r.isLockedOut && (
                  <Tooltip title="Mở khóa">
                    <Button
                      size="small"
                      icon={<UnlockOutlined />}
                      onClick={() =>
                        unlock.mutateAsync(r.id).then(() => message.success('Đã mở khóa'), onError)
                      }
                    />
                  </Tooltip>
                )}
                <Popconfirm
                  title="Đặt lại mật khẩu?"
                  description="Mật khẩu tạm sẽ được gửi qua email và người dùng phải đổi khi đăng nhập."
                  okText="Đặt lại"
                  cancelText="Hủy"
                  onConfirm={async () => {
                    try {
                      const result = await reset.mutateAsync(r.id);
                      modal.success({
                        title: 'Đã đặt lại mật khẩu',
                        content: result.emailSent
                          ? 'Mật khẩu tạm đã được gửi tới email của người dùng.'
                          : `Người dùng chưa có email. Mật khẩu tạm: ${result.temporaryPassword}`,
                      });
                    } catch (e) {
                      onError(e);
                    }
                  }}
                >
                  <Tooltip title="Đặt lại mật khẩu">
                    <Button size="small" icon={<KeyOutlined />} />
                  </Tooltip>
                </Popconfirm>
                <HistoryButton url={`${resources.users}/${r.id}/history`} title={r.fullName} />
              </Space>
            ),
          },
        ]}
      />

      <Drawer
        title={editingId === 'new' ? 'Thêm người dùng' : 'Sửa người dùng'}
        open={!!editingId}
        onClose={() => setEditingId(null)}
        width={720}
        loading={editingId !== 'new' && detail.isLoading}
        destroyOnHidden
        extra={
          <Space>
            <Button onClick={() => setEditingId(null)}>Hủy</Button>
            <Button type="primary" loading={save.isPending} onClick={() => form.submit()}>
              Lưu
            </Button>
          </Space>
        }
      >
        <Form form={form} layout="vertical" onFinish={submit}>
          <Form.Item name="id" hidden>
            <Input />
          </Form.Item>
          <Row gutter={16}>
            <Col xs={24} md={12}>
              <Form.Item
                name="userName"
                label="Tên đăng nhập"
                rules={[{ required: true, message: 'Bắt buộc' }, USER_NAME_RULE]}
              >
                <Input disabled={editingId !== 'new'} />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item name="userType" label="Loại tài khoản" rules={[{ required: true }]}>
                <Select
                  disabled={editingId !== 'new'}
                  options={statusOptions('userType')}
                  onChange={() => form.setFieldValue('roleIds', [])}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item name="fullName" label="Họ tên" rules={[{ required: true, message: 'Bắt buộc' }]}>
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item name="jobTitle" label="Chức danh">
                <Input placeholder="VD: Phó Giám đốc" />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item
                name="email"
                label="Email"
                rules={[{ type: 'email', message: 'Email không hợp lệ' }]}
              >
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item name="phone" label="Điện thoại">
                <Input />
              </Form.Item>
            </Col>
            {userType === 'Supplier' ? (
              <Col span={24}>
                <Form.Item
                  name="supplierId"
                  label="Nhà cung cấp"
                  rules={[{ required: true, message: 'Chọn nhà cung cấp' }]}
                >
                  <Select
                    disabled={editingId !== 'new'}
                    showSearch
                    optionFilterProp="label"
                    options={(suppliers.data ?? []).map((s) => ({
                      value: s.id,
                      label: `${s.name} (${s.code})`,
                    }))}
                  />
                </Form.Item>
              </Col>
            ) : (
              <>
                <Col xs={24} md={12}>
                  <Form.Item name="facilityId" label="Cơ sở">
                    <Select allowClear options={facilityOptions} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item name="departmentId" label="Khoa/phòng">
                    <Select allowClear showSearch optionFilterProp="label" options={departmentOptions} />
                  </Form.Item>
                </Col>
              </>
            )}
            <Col span={24}>
              <Form.Item
                name="roleIds"
                label="Vai trò"
                rules={[{ required: true, message: 'Chọn ít nhất một vai trò' }]}
              >
                <Select mode="multiple" options={roleOptions} optionFilterProp="label" />
              </Form.Item>
            </Col>
            {editingId !== 'new' && (
              <Col xs={24} md={12}>
                <Form.Item name="status" label="Trạng thái">
                  <Select options={statusOptions('userStatus')} />
                </Form.Item>
              </Col>
            )}
            {editingId === 'new' && (
              <Col xs={24} md={12}>
                <Form.Item
                  name="password"
                  label="Mật khẩu ban đầu"
                  extra="Để trống để hệ thống tự sinh và gửi qua email"
                  rules={[PASSWORD_RULE]}
                >
                  <Input.Password autoComplete="new-password" />
                </Form.Item>
              </Col>
            )}
          </Row>

          {userType !== 'Supplier' && (
            <>
              <Typography.Title level={5}>Phạm vi dữ liệu</Typography.Title>
              <Alert
                type="info"
                showIcon
                style={{ marginBottom: 12 }}
                message="Giới hạn dữ liệu người dùng được thấy, ví dụ Tổ trưởng Dược chỉ thấy tổng hợp đề xuất mảng Thuốc. Để trống = không giới hạn."
              />
              <Row gutter={16}>
                <Col xs={24} md={8}>
                  <Form.Item name="facilityScopes" label="Cơ sở">
                    <Select mode="multiple" options={facilityOptions} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={8}>
                  <Form.Item name="departmentScopes" label="Khoa/phòng">
                    <Select mode="multiple" optionFilterProp="label" options={departmentOptions} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={8}>
                  <Form.Item name="domainScopes" label="Mảng phụ trách">
                    <Select mode="multiple" options={DOMAIN_OPTIONS} />
                  </Form.Item>
                </Col>
              </Row>
            </>
          )}

          {editingId !== 'new' && (
            <>
              <Typography.Title level={5}>Chứng thư số đã đăng ký</Typography.Title>
              <Row gutter={16}>
                <Col xs={24} md={8}>
                  <Form.Item name="certificateSerial" label="Số serial">
                    <Input />
                  </Form.Item>
                </Col>
                <Col xs={24} md={10}>
                  <Form.Item name="certificateIssuer" label="Tổ chức cấp">
                    <Input />
                  </Form.Item>
                </Col>
                <Col xs={24} md={6}>
                  <Form.Item name="certificateValidTo" label="Hết hạn (yyyy-mm-dd)">
                    <Input placeholder="2027-12-31" />
                  </Form.Item>
                </Col>
              </Row>
            </>
          )}
        </Form>
      </Drawer>
    </>
  );
}
