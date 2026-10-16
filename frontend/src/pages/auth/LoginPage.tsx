import { Alert, Button, Card, Form, Input, Typography } from 'antd';
import { LockOutlined, SafetyOutlined, UserOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import { useLogin, useLoginTwoFactor } from '../../api/auth';
import { toApiProblem } from '../../api/http';
import { useAuth } from '../../auth/useAuth';

export function LoginPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const { isAuthenticated } = useAuth();
  const login = useLogin();
  const twoFactor = useLoginTwoFactor();
  const [challenge, setChallenge] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const returnUrl = params.get('returnUrl');
  const safeReturn = returnUrl && returnUrl.startsWith('/') && !returnUrl.startsWith('//') ? returnUrl : '/';

  if (isAuthenticated) return <Navigate to={safeReturn} replace />;

  const done = (mustChange?: boolean) =>
    navigate(mustChange ? '/doi-mat-khau' : safeReturn, { replace: true });

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'grid',
        placeItems: 'center',
        padding: 16,
        background: 'linear-gradient(135deg, #0d3b7a 0%, #1554ad 55%, #3d7fd6 100%)',
      }}
    >
      <Card style={{ width: '100%', maxWidth: 400 }}>
        <Typography.Title level={3} style={{ marginBottom: 4, color: '#1554ad' }}>
          BlueProcure
        </Typography.Title>
        <Typography.Paragraph type="secondary">
          Cổng Quản lý & Thanh toán Mua sắm Hàng hóa Trực tuyến
        </Typography.Paragraph>

        {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 16 }} />}

        {!challenge ? (
          <Form
            layout="vertical"
            requiredMark={false}
            onFinish={async (values: { userName: string; password: string }) => {
              setError(null);
              try {
                const result = await login.mutateAsync(values);
                if (result.requiresTwoFactor) setChallenge(result.twoFactorChallenge ?? null);
                else done(result.user?.mustChangePassword);
              } catch (e) {
                setError(toApiProblem(e).title);
              }
            }}
          >
            <Form.Item
              name="userName"
              label="Tên đăng nhập"
              rules={[{ required: true, message: 'Vui lòng nhập tên đăng nhập' }]}
            >
              <Input prefix={<UserOutlined />} autoComplete="username" autoFocus size="large" />
            </Form.Item>
            <Form.Item
              name="password"
              label="Mật khẩu"
              rules={[{ required: true, message: 'Vui lòng nhập mật khẩu' }]}
            >
              <Input.Password prefix={<LockOutlined />} autoComplete="current-password" size="large" />
            </Form.Item>
            <Button type="primary" htmlType="submit" block size="large" loading={login.isPending}>
              Đăng nhập
            </Button>
          </Form>
        ) : (
          <Form
            layout="vertical"
            requiredMark={false}
            onFinish={async (values: { code: string }) => {
              setError(null);
              try {
                const result = await twoFactor.mutateAsync({ challenge, code: values.code.trim() });
                done(result.user?.mustChangePassword);
              } catch (e) {
                const problem = toApiProblem(e);
                setError(problem.title);
                if (problem.code === 'TWO_FACTOR_CHALLENGE_EXPIRED' || problem.code === 'ACCOUNT_LOCKED')
                  setChallenge(null);
              }
            }}
          >
            <Typography.Paragraph>
              Nhập mã 6 chữ số trong ứng dụng xác thực (Google/Microsoft Authenticator).
            </Typography.Paragraph>
            <Form.Item
              name="code"
              label="Mã xác thực"
              rules={[{ required: true, pattern: /^\d{6}$/, message: 'Mã gồm 6 chữ số' }]}
            >
              <Input
                prefix={<SafetyOutlined />}
                inputMode="numeric"
                maxLength={6}
                autoFocus
                size="large"
                autoComplete="one-time-code"
              />
            </Form.Item>
            <Button type="primary" htmlType="submit" block size="large" loading={twoFactor.isPending}>
              Xác nhận
            </Button>
            <Button type="link" block onClick={() => setChallenge(null)}>
              Quay lại
            </Button>
          </Form>
        )}
      </Card>
    </div>
  );
}
