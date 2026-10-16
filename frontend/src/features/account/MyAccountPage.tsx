import {
  Alert,
  App,
  Button,
  Card,
  Col,
  Descriptions,
  Form,
  Input,
  QRCode,
  Row,
  Space,
  Steps,
  Tag,
  Typography,
} from 'antd';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  reloadCurrentUser,
  useDisableTwoFactor,
  useEnableTwoFactor,
  useTwoFactorSetup,
} from '../../api/auth';
import { authStore } from '../../auth/authStore';
import { useAuth } from '../../auth/useAuth';
import { PageHeader, useApiError } from '../../components/common';
import { ChangePasswordForm } from '../../pages/auth/ChangePasswordPage';

function TwoFactorCard() {
  const { user } = useAuth();
  const setup = useTwoFactorSetup();
  const enable = useEnableTwoFactor();
  const disable = useDisableTwoFactor();
  const [pending, setPending] = useState<{ secret: string; otpAuthUri: string } | null>(null);
  const { message } = App.useApp();
  const onError = useApiError();

  if (user?.twoFactorEnabled) {
    return (
      <Card title="Xác thực 2 lớp (TOTP)">
        <Space direction="vertical" style={{ width: '100%' }}>
          <Tag color="green">Đang bật</Tag>
          <Typography.Paragraph type="secondary">
            Mỗi lần đăng nhập cần nhập mã 6 số từ ứng dụng xác thực.
          </Typography.Paragraph>
          <Form
            layout="inline"
            onFinish={async (v: { password: string }) => {
              try {
                await disable.mutateAsync({ password: v.password });
                await reloadCurrentUser();
                message.success('Đã tắt xác thực 2 lớp');
              } catch (e) {
                onError(e);
              }
            }}
          >
            <Form.Item name="password" rules={[{ required: true, message: 'Nhập mật khẩu để xác nhận' }]}>
              <Input.Password placeholder="Mật khẩu hiện tại" />
            </Form.Item>
            <Button danger htmlType="submit" loading={disable.isPending}>
              Tắt 2FA
            </Button>
          </Form>
        </Space>
      </Card>
    );
  }

  return (
    <Card title="Xác thực 2 lớp (TOTP)">
      {!pending ? (
        <Space direction="vertical">
          <Typography.Paragraph>
            Bảo vệ tài khoản bằng mã 6 số từ ứng dụng Google Authenticator, Microsoft Authenticator…
          </Typography.Paragraph>
          <Button
            type="primary"
            loading={setup.isPending}
            onClick={async () => {
              try {
                setPending(await setup.mutateAsync());
              } catch (e) {
                onError(e);
              }
            }}
          >
            Thiết lập 2FA
          </Button>
        </Space>
      ) : (
        <>
          <Steps
            direction="vertical"
            size="small"
            items={[
              { title: 'Quét mã QR bằng ứng dụng xác thực', status: 'process' },
              { title: 'Nhập mã 6 số để xác nhận', status: 'process' },
            ]}
          />
          <Space align="start" wrap style={{ marginTop: 12 }}>
            <QRCode value={pending.otpAuthUri} size={168} />
            <Space direction="vertical">
              <Typography.Text type="secondary">Hoặc nhập khóa thủ công:</Typography.Text>
              <Typography.Text code copyable>
                {pending.secret}
              </Typography.Text>
              <Form
                layout="inline"
                onFinish={async (v: { code: string }) => {
                  try {
                    await enable.mutateAsync({ code: v.code.trim() });
                    // Enabling 2FA rotates the security stamp: sign in again.
                    message.success('Đã bật xác thực 2 lớp. Vui lòng đăng nhập lại.');
                    authStore.clear();
                    window.location.assign('/dang-nhap');
                  } catch (e) {
                    onError(e);
                  }
                }}
              >
                <Form.Item
                  name="code"
                  rules={[{ required: true, pattern: /^\d{6}$/, message: 'Mã gồm 6 chữ số' }]}
                >
                  <Input placeholder="Mã 6 số" maxLength={6} inputMode="numeric" style={{ width: 140 }} />
                </Form.Item>
                <Button type="primary" htmlType="submit" loading={enable.isPending}>
                  Xác nhận
                </Button>
              </Form>
            </Space>
          </Space>
        </>
      )}
    </Card>
  );
}

export function MyAccountPage() {
  const { user } = useAuth();
  const navigate = useNavigate();
  return (
    <>
      <PageHeader title="Tài khoản & bảo mật" />
      <Row gutter={[16, 16]}>
        <Col xs={24} lg={12}>
          <Card title="Thông tin tài khoản">
            <Descriptions column={1} size="small">
              <Descriptions.Item label="Tên đăng nhập">{user?.userName}</Descriptions.Item>
              <Descriptions.Item label="Họ tên">{user?.fullName}</Descriptions.Item>
              <Descriptions.Item label="Chức danh">{user?.jobTitle}</Descriptions.Item>
              <Descriptions.Item label="Email">{user?.email}</Descriptions.Item>
              {user?.supplierName && (
                <Descriptions.Item label="Nhà cung cấp">{user.supplierName}</Descriptions.Item>
              )}
              {user?.departmentName && (
                <Descriptions.Item label="Khoa/phòng">{user.departmentName}</Descriptions.Item>
              )}
              {user?.facilityName && <Descriptions.Item label="Cơ sở">{user.facilityName}</Descriptions.Item>}
              <Descriptions.Item label="Vai trò">
                {user?.roleNames.map((r) => (
                  <Tag key={r}>{r}</Tag>
                ))}
              </Descriptions.Item>
            </Descriptions>
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <TwoFactorCard />
        </Col>
        <Col xs={24} lg={12}>
          <Card title="Đổi mật khẩu">
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 16 }}
              message="Sau khi đổi mật khẩu, mọi phiên đăng nhập sẽ kết thúc."
            />
            <ChangePasswordForm
              onDone={() => {
                authStore.clear();
                navigate('/dang-nhap', { replace: true });
              }}
            />
          </Card>
        </Col>
      </Row>
    </>
  );
}
