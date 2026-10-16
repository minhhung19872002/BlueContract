import { Alert, App, Button, Form, Input } from 'antd';
import { Circle, CircleCheck } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { useChangePassword } from '../../api/auth';
import { toApiProblem } from '../../api/http';
import { useAuth } from '../../auth/useAuth';
import { authStore } from '../../auth/authStore';
import { PASSWORD_RULE, PASSWORD_STRENGTH, passwordChecks } from '../../utils/validation';
import './auth.css';

/** Checklist of the 5 rules and the strength bar (design/Doi mat khau.dc.html). */
function PasswordStrength({ value }: { value: string }) {
  const checks = passwordChecks(value);
  const ok = checks.filter((c) => c.ok).length;
  const tone = ok <= 2 ? 'danger' : ok <= 4 ? 'warning' : 'success';
  return (
    <div className="pw-panel">
      <div className="pw-panel__head">
        <span>Độ mạnh mật khẩu</span>
        <span style={{ color: `var(--bp-${tone}-fg)` }}>{value ? PASSWORD_STRENGTH[ok] : '—'}</span>
      </div>
      <div className="pw-panel__bars" aria-hidden>
        {checks.map((_, i) => (
          <span key={i} style={{ background: i < ok ? `var(--bp-${tone}-solid)` : 'var(--bp-border)' }} />
        ))}
      </div>
      <ul className="pw-panel__rules" aria-label="Quy tắc mật khẩu">
        {checks.map((c) => (
          <li key={c.label} className={c.ok ? 'is-ok' : undefined}>
            {c.ok ? <CircleCheck size={14} aria-hidden /> : <Circle size={14} aria-hidden />}
            {c.label}
            <span className="sr-only">{c.ok ? ' — đạt' : ' — chưa đạt'}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

/** Password change form. After success the session is closed and the user signs in again with the new password. */
export function ChangePasswordForm({ onDone, footer }: { onDone: () => void; footer?: React.ReactNode }) {
  const change = useChangePassword();
  const { message } = App.useApp();
  const [form] = Form.useForm();
  const newPassword = Form.useWatch('newPassword', form) ?? '';
  const confirm = Form.useWatch('confirm', form) ?? '';
  const current = Form.useWatch('currentPassword', form) ?? '';
  const valid = !!current && passwordChecks(newPassword).every((c) => c.ok) && confirm === newPassword;

  return (
    <Form
      form={form}
      layout="vertical"
      requiredMark
      className="pw-form"
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
      <PasswordStrength value={newPassword} />
      <div className="pw-form__actions">
        {footer}
        <Button type="primary" htmlType="submit" loading={change.isPending} disabled={!valid}>
          Đổi mật khẩu
        </Button>
      </div>
    </Form>
  );
}

export function ChangePasswordPage() {
  const navigate = useNavigate();
  const { user, logout } = useAuth();
  return (
    <div className="pw-page">
      <div className="pw-page__brand">
        <img src="/favicon.svg" width={32} height={32} alt="" />
        <span>BlueProcure</span>
      </div>
      <section className="pw-page__card">
        <div>
          <h1 className="pw-page__title">Đổi mật khẩu</h1>
          {user && (
            <p className="pw-page__lead">
              Tài khoản <span className="mono">{user.userName}</span> · {user.fullName}
            </p>
          )}
        </div>
        {user?.mustChangePassword && (
          <Alert
            type="warning"
            showIcon
            message="Bạn đang dùng mật khẩu tạm thời. Vui lòng đặt mật khẩu mới trước khi sử dụng hệ thống."
          />
        )}
        <ChangePasswordForm
          onDone={() => {
            authStore.clear();
            navigate('/dang-nhap', { replace: true });
          }}
          footer={
            <Button
              onClick={async () => {
                await logout();
                navigate('/dang-nhap', { replace: true });
              }}
            >
              Đăng xuất
            </Button>
          }
        />
        <p className="pw-page__note">
          Sau khi đổi thành công, phiên hiện tại sẽ đóng và bạn đăng nhập lại bằng mật khẩu mới.
        </p>
      </section>
    </div>
  );
}
