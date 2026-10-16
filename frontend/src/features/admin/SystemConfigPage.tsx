import {
  App,
  Button,
  Col,
  Form,
  Input,
  InputNumber,
  Popconfirm,
  Row,
  Select,
  Space,
  Switch,
  Tag,
} from 'antd';
import { RotateCcw, Save, Send } from 'lucide-react';
import { useEffect, useState } from 'react';
import {
  SYSTEM_CONFIG,
  sendTestMail,
  useAction,
  useGet,
  type MailSettingsDto,
  type SaveMailSettingsRequest,
  type SaveSigningSettingsRequest,
  type SigningSettingsDto,
} from '../../api/admin';
import { useAuth } from '../../auth/useAuth';
import { PageHeader, useApiError } from '../../components/common';
import { Banner, Panel } from '../../components/layout';
import './systemConfig.css';

const MAIL = `${SYSTEM_CONFIG}/mail`;
const SIGNING = `${SYSTEM_CONFIG}/signing`;

const isFormError = (e: unknown) => !!e && typeof e === 'object' && 'errorFields' in e;

/** Cấu hình email (SMTP) và ký số (spec §14). Giá trị lưu ở đây thay cho cấu hình máy chủ. */
export function SystemConfigPage() {
  return (
    <>
      <PageHeader
        title="Cấu hình email & ký số"
        subtitle="Giá trị lưu tại đây được dùng thay cho cấu hình máy chủ (appsettings / biến môi trường)"
      />
      <Space direction="vertical" size={16} style={{ width: '100%' }}>
        <MailPanel />
        <SigningPanel />
      </Space>
    </>
  );
}

function SourceTag({ overridden }: { overridden: boolean }) {
  return overridden ? <Tag color="blue">Đã chỉnh trên giao diện</Tag> : <Tag>Theo cấu hình máy chủ</Tag>;
}

function ResetButton({ url, what }: { url: string; what: string }) {
  const reset = useAction(SYSTEM_CONFIG, () => ({ url, method: 'delete' }));
  const { message } = App.useApp();
  const onError = useApiError();
  return (
    <Popconfirm
      title={`Bỏ cấu hình ${what} đã chỉnh?`}
      description="Hệ thống sẽ dùng lại cấu hình của máy chủ."
      onConfirm={async () => {
        try {
          await reset.mutateAsync();
          message.success('Đã dùng lại cấu hình máy chủ');
        } catch (e) {
          onError(e);
        }
      }}
    >
      <Button icon={<RotateCcw size={14} aria-hidden />}>Dùng cấu hình máy chủ</Button>
    </Popconfirm>
  );
}

type MailForm = Omit<SaveMailSettingsRequest, 'password' | 'userName'> & {
  userName?: string;
  password?: string;
};

function MailPanel() {
  const current = useGet<MailSettingsDto>(MAIL, [SYSTEM_CONFIG, 'mail']);
  const save = useAction<SaveMailSettingsRequest>(SYSTEM_CONFIG, (body) => ({
    url: MAIL,
    method: 'put',
    body,
  }));
  const [form] = Form.useForm<MailForm>();
  const [typedTo, setTestTo] = useState<string | null>(null);
  const [testing, setTesting] = useState(false);
  const { user } = useAuth();
  const { message } = App.useApp();
  const onError = useApiError();
  // Defaults to the admin's own address until something is typed.
  const testTo = typedTo ?? user?.email ?? '';

  useEffect(() => {
    if (current.data)
      form.setFieldsValue({
        ...current.data,
        userName: current.data.userName ?? undefined,
        password: undefined,
      });
  }, [current.data, form]);

  /** Empty password field = keep the saved one. */
  const request = (v: MailForm): SaveMailSettingsRequest => ({
    ...v,
    userName: v.userName?.trim() || null,
    password: v.password ? v.password : null,
  });

  return (
    <Panel
      title="Máy chủ gửi email (SMTP)"
      extra={current.data && <SourceTag overridden={current.data.isOverridden} />}
    >
      <Form
        form={form}
        layout="vertical"
        disabled={current.isLoading}
        onFinish={async (v) => {
          try {
            await save.mutateAsync(request(v));
            message.success('Đã lưu cấu hình email');
          } catch (e) {
            onError(e);
          }
        }}
      >
        <Form.Item
          name="enabled"
          label="Gửi email"
          valuePropName="checked"
          extra="Tắt: hệ thống không gửi email nào (thông báo vẫn hiện trên cổng)."
        >
          <Switch />
        </Form.Item>
        <Row gutter={16}>
          <Col xs={24} md={12}>
            <Form.Item name="host" label="Máy chủ SMTP" rules={[{ required: true, whitespace: true }]}>
              <Input placeholder="smtp.benhvien.vn" />
            </Form.Item>
          </Col>
          <Col xs={12} md={6}>
            <Form.Item name="port" label="Cổng" rules={[{ required: true }]}>
              <InputNumber min={1} max={65535} precision={0} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col xs={12} md={6}>
            <Form.Item name="useStartTls" label="Mã hóa STARTTLS" valuePropName="checked">
              <Switch />
            </Form.Item>
          </Col>
          <Col xs={24} md={12}>
            <Form.Item
              name="userName"
              label="Tài khoản đăng nhập SMTP"
              extra="Để trống nếu máy chủ không yêu cầu đăng nhập."
            >
              <Input autoComplete="off" />
            </Form.Item>
          </Col>
          <Col xs={24} md={12}>
            <Form.Item
              name="password"
              label="Mật khẩu SMTP"
              extra={
                current.data?.hasPassword ? 'Đã có mật khẩu; để trống để giữ nguyên.' : 'Chưa có mật khẩu.'
              }
            >
              <Input.Password
                autoComplete="new-password"
                placeholder={current.data?.hasPassword ? '••••••••' : undefined}
              />
            </Form.Item>
          </Col>
          <Col xs={24} md={12}>
            <Form.Item
              name="fromAddress"
              label="Địa chỉ người gửi"
              rules={[
                { required: true, whitespace: true },
                { type: 'email', message: 'Email không hợp lệ' },
              ]}
            >
              <Input placeholder="no-reply@benhvien.vn" />
            </Form.Item>
          </Col>
          <Col xs={24} md={12}>
            <Form.Item name="fromName" label="Tên người gửi" rules={[{ required: true, whitespace: true }]}>
              <Input />
            </Form.Item>
          </Col>
        </Row>
        <div className="sysconfig__test">
          <Input
            value={testTo}
            onChange={(e) => setTestTo(e.target.value)}
            placeholder="Email nhận thư thử"
            aria-label="Email nhận thư thử"
            type="email"
          />
          <Button
            icon={<Send size={14} aria-hidden />}
            loading={testing}
            disabled={!testTo.trim()}
            onClick={async () => {
              setTesting(true);
              try {
                await sendTestMail(testTo.trim(), request(await form.validateFields()));
                message.success(`Đã gửi thư thử tới ${testTo.trim()}`);
              } catch (e) {
                if (!isFormError(e)) onError(e);
              } finally {
                setTesting(false);
              }
            }}
          >
            Gửi thư thử
          </Button>
        </div>
        <Space wrap className="sysconfig__actions">
          <Button
            type="primary"
            htmlType="submit"
            icon={<Save size={14} aria-hidden />}
            loading={save.isPending}
          >
            Lưu cấu hình email
          </Button>
          {current.data?.isOverridden && <ResetButton url={MAIL} what="email" />}
        </Space>
      </Form>
    </Panel>
  );
}

type SigningForm = Omit<SaveSigningSettingsRequest, 'remoteCas' | 'tsaUrl'> & {
  tsaUrl?: string;
  remoteCas: {
    code: string;
    name: string;
    baseUrl?: string;
    clientId?: string;
    clientSecret?: string;
    hasSecret: boolean;
  }[];
};

function SigningPanel() {
  const current = useGet<SigningSettingsDto>(SIGNING, [SYSTEM_CONFIG, 'signing']);
  const save = useAction<SaveSigningSettingsRequest>(SYSTEM_CONFIG, (body) => ({
    url: SIGNING,
    method: 'put',
    body,
  }));
  const [form] = Form.useForm<SigningForm>();
  const { message } = App.useApp();
  const onError = useApiError();
  const s = current.data;

  useEffect(() => {
    if (!s) return;
    form.setFieldsValue({
      defaultProvider: s.defaultProvider,
      tsaUrl: s.tsaUrl ?? undefined,
      initialRequiresCa: s.initialRequiresCa,
      mockRemoteDelaySeconds: s.mockRemoteDelaySeconds,
      remoteCas: s.remoteCas.map((c) => ({
        code: c.code,
        name: c.name,
        baseUrl: c.baseUrl ?? undefined,
        clientId: c.clientId ?? undefined,
        clientSecret: undefined,
        hasSecret: c.hasSecret,
      })),
    });
  }, [s, form]);

  return (
    <Panel title="Ký số" extra={s && <SourceTag overridden={s.isOverridden} />}>
      {s?.enableMockProviders && (
        <div className="sysconfig__banner">
          <Banner tone="warning" title="Đang bật chữ ký thử nghiệm (MOCK)">
            Chữ ký thử nghiệm không có giá trị pháp lý. Trên máy chủ thật, đặt{' '}
            <code>SIGNING_ENABLE_MOCK=false</code> và nhập thông tin kết nối của nhà cung cấp chữ ký số bên
            dưới.
          </Banner>
        </div>
      )}
      <Form
        form={form}
        layout="vertical"
        disabled={current.isLoading}
        onFinish={async (v) => {
          try {
            await save.mutateAsync({
              defaultProvider: v.defaultProvider,
              tsaUrl: v.tsaUrl?.trim() || null,
              initialRequiresCa: v.initialRequiresCa,
              mockRemoteDelaySeconds: v.mockRemoteDelaySeconds,
              remoteCas: v.remoteCas.map((c) => ({
                code: c.code,
                baseUrl: c.baseUrl?.trim() || null,
                clientId: c.clientId?.trim() || null,
                clientSecret: c.clientSecret ? c.clientSecret : null,
              })),
            });
            message.success('Đã lưu cấu hình ký số');
          } catch (e) {
            onError(e);
          }
        }}
      >
        <Row gutter={16}>
          <Col xs={24} md={12}>
            <Form.Item name="defaultProvider" label="Nhà cung cấp mặc định" rules={[{ required: true }]}>
              <Select options={(s?.providers ?? []).map((p) => ({ value: p.code, label: p.name }))} />
            </Form.Item>
          </Col>
          <Col xs={24} md={12}>
            <Form.Item
              name="tsaUrl"
              label="Máy chủ dấu thời gian (TSA)"
              extra="Để trống: chữ ký không gắn dấu thời gian RFC 3161."
              rules={[{ type: 'url', message: 'Phải là URL http/https' }]}
            >
              <Input placeholder="http://tsa.ca.gov.vn" />
            </Form.Item>
          </Col>
          <Col xs={24} md={12}>
            <Form.Item
              name="initialRequiresCa"
              label="Ký nháy phải dùng chữ ký số của CA"
              valuePropName="checked"
              extra="Tắt: ký nháy bằng chứng thư nội bộ của hệ thống, không cần thiết bị ký."
            >
              <Switch />
            </Form.Item>
          </Col>
          {s?.enableMockProviders && (
            <Col xs={24} md={12}>
              <Form.Item
                name="mockRemoteDelaySeconds"
                label="Thời gian xác nhận giả lập trên điện thoại (giây)"
                rules={[{ required: true }]}
              >
                <InputNumber min={0} max={300} precision={0} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          )}
        </Row>
        <div className="sysconfig__subtitle">
          Ký số từ xa (thông tin do nhà cung cấp cấp khi bệnh viện đăng ký dịch vụ)
        </div>
        <Form.List name="remoteCas">
          {(rows) => rows.map(({ key, name }) => <RemoteCaRow key={key} name={name} />)}
        </Form.List>
        <Space wrap className="sysconfig__actions">
          <Button
            type="primary"
            htmlType="submit"
            icon={<Save size={14} aria-hidden />}
            loading={save.isPending}
          >
            Lưu cấu hình ký số
          </Button>
          {s?.isOverridden && <ResetButton url={SIGNING} what="ký số" />}
        </Space>
      </Form>
    </Panel>
  );
}

function RemoteCaRow({ name }: { name: number }) {
  const form = Form.useFormInstance<SigningForm>();
  const ca = Form.useWatch(['remoteCas', name], form);
  return (
    <fieldset className="sysconfig__ca">
      <legend>{ca?.name}</legend>
      <Row gutter={16}>
        <Col xs={24} md={10}>
          <Form.Item
            name={[name, 'baseUrl']}
            label="Địa chỉ API"
            rules={[{ type: 'url', message: 'Phải là URL http/https' }]}
          >
            <Input placeholder="https://..." />
          </Form.Item>
        </Col>
        <Col xs={24} md={7}>
          <Form.Item name={[name, 'clientId']} label="Mã ứng dụng (Client ID)">
            <Input autoComplete="off" />
          </Form.Item>
        </Col>
        <Col xs={24} md={7}>
          <Form.Item
            name={[name, 'clientSecret']}
            label="Khóa bí mật (Client secret)"
            extra={ca?.hasSecret ? 'Đã có; để trống để giữ nguyên.' : undefined}
          >
            <Input.Password
              autoComplete="new-password"
              placeholder={ca?.hasSecret ? '••••••••' : undefined}
            />
          </Form.Item>
        </Col>
      </Row>
    </fieldset>
  );
}
