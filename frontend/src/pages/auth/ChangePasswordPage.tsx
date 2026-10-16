import { Alert, App, Button, Card, Form, Input, Typography } from 'antd';
import { useNavigate } from 'react-router-dom';
import { useChangePassword } from '../../api/auth';
import { toApiProblem } from '../../api/http';
import { useAuth } from '../../auth/useAuth';
import { authStore } from '../../auth/authStore';
import { PASSWORD_RULE } from '../../utils/validation';

/** Password change form. After success the session is closed and the user signs in again with the new password. */
export function ChangePasswordForm({ onDone }: { onDone: () => void }) {
  const change = useChangePassword();
  const { message } = App.useApp();
  const [form] = Form.useForm();

  return (
    <Form
      form={form}
      layout="vertical"
      style={{ maxWidth: 420 }}
      onFinish={async (v: { currentPassword: string; newPassword: string }) => {
        try {
          await change.mutateAsync({ currentPassword: v.currentPassword, newPassword: v.newPassword });
          message.success('Đổi mật khẩu thành công. Vui lòng đăng nhập lại.');
          onDone();
        } catch (e) {
          const p = toApiProblem(e);
          message.error(p.errors ? Object.values(p.errors).flat()[0] : p.title);
        }
      }}
    >
      <Form.Item
        name="currentPassword"
        label="Mật khẩu hiện tại"
        rules={[{ required: true, message: 'Vui lòng nhập mật khẩu hiện tại' }]}
      >
        <Input.Password autoComplete="current-password" />
      </Form.Item>
      <Form.Item
        name="newPassword"
        label="Mật khẩu mới"
        rules={[{ required: true, message: 'Vui lòng nhập mật khẩu mới' }, PASSWORD_RULE]}
      >
        <Input.Password autoComplete="new-password" />
      </Form.Item>
      <Form.Item
        name="confirm"
        label="Nhập lại mật khẩu mới"
        dependencies={['newPassword']}
        rules={[
          { required: true, message: 'Vui lòng nhập lại mật khẩu mới' },
          ({ getFieldValue }) => ({
            validator: (_, value) =>
              !value || getFieldValue('newPassword') === value
                ? Promise.resolve()
                : Promise.reject(new Error('Mật khẩu nhập lại không khớp')),
          }),
        ]}
      >
        <Input.Password autoComplete="new-password" />
      </Form.Item>
      <Button type="primary" htmlType="submit" loading={change.isPending}>
        Đổi mật khẩu
      </Button>
    </Form>
  );
}

export function ChangePasswordPage() {
  const navigate = useNavigate();
  const { user } = useAuth();
  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'grid',
        placeItems: 'center',
        padding: 16,
        background: '#f5f7fa',
      }}
    >
      <Card style={{ width: '100%', maxWidth: 480 }}>
        <Typography.Title level={4}>Đổi mật khẩu</Typography.Title>
        {user?.mustChangePassword && (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 16 }}
            message="Bạn đang dùng mật khẩu tạm thời. Vui lòng đặt mật khẩu mới trước khi sử dụng hệ thống."
          />
        )}
        <ChangePasswordForm
          onDone={() => {
            authStore.clear();
            navigate('/dang-nhap', { replace: true });
          }}
        />
      </Card>
    </div>
  );
}
