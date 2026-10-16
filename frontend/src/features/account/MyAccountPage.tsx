import { App, Button, Form, Input, QRCode, Switch, Tag, Typography } from 'antd';
import { ShieldCheck } from 'lucide-react';
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
import { InfoGrid } from '../../components/layout';
import { SignatureImageSlot } from '../../components/SignatureImageSlot';
import { MyCertificateCard } from './MyCertificateCard';
import { ChangePasswordForm } from '../../pages/auth/ChangePasswordPage';
import './account.css';

function TwoFactorCard() {
  const { user } = useAuth();
  const setup = useTwoFactorSetup();
  const enable = useEnableTwoFactor();
  const disable = useDisableTwoFactor();
  const [pending, setPending] = useState<{ secret: string; otpAuthUri: string } | null>(null);
  const [disabling, setDisabling] = useState(false);
  const { message } = App.useApp();
  const onError = useApiError();
  const on = !!user?.twoFactorEnabled;

  const startSetup = async () => {
    try {
      setPending(await setup.mutateAsync());
    } catch (e) {
      onError(e);
    }
  };

  return (
    <section className="account__card">
      <div className="account__card-head">
        <div>
          <h2 className="account__title">Xác thực 2 lớp (TOTP)</h2>
          <p className="account__lead">Google / Microsoft Authenticator</p>
        </div>
        <label className="account__switch">
          <span style={{ color: on ? 'var(--bp-success-fg)' : 'var(--bp-text-2)' }}>
            {on ? 'Đang bật' : 'Đang tắt'}
          </span>
          <Switch
            checked={on || !!pending || disabling}
            loading={setup.isPending}
            aria-label="Xác thực 2 lớp"
            onChange={(v) => {
              if (on) setDisabling(!v);
              else if (v) void startSetup();
              else setPending(null);
            }}
          />
        </label>
      </div>
      {on && !disabling && (
        <div className="account__ok">
          <ShieldCheck size={16} aria-hidden />
          Đã bật. Mỗi lần đăng nhập cần nhập mã 6 số từ ứng dụng xác thực.
        </div>
      )}
      {on && disabling && (
        <Form
          layout="vertical"
          className="account__box"
          onFinish={async (v: { password: string }) => {
            try {
              await disable.mutateAsync({ password: v.password });
              await reloadCurrentUser();
              setDisabling(false);
              message.success('Đã tắt xác thực 2 lớp');
            } catch (e) {
              onError(e);
            }
          }}
        >
          <Form.Item
            name="password"
            label="Nhập mật khẩu để tắt xác thực 2 lớp"
            rules={[{ required: true, message: 'Nhập mật khẩu để xác nhận' }]}
          >
            <Input.Password placeholder="Mật khẩu hiện tại" autoComplete="current-password" />
          </Form.Item>
          <div className="account__actions">
            <Button onClick={() => setDisabling(false)}>Giữ nguyên</Button>
            <Button danger htmlType="submit" loading={disable.isPending}>
              Tắt xác thực 2 lớp
            </Button>
          </div>
        </Form>
      )}
      {!on && !pending && (
        <p className="account__lead">
          Bảo vệ tài khoản bằng mã 6 số từ ứng dụng xác thực. Bật công tắc để bắt đầu thiết lập.
        </p>
      )}
      {!on && pending && (
        <div className="account__box account__setup">
          <QRCode value={pending.otpAuthUri} size={140} bgColor="#fff" />
          <div className="account__setup-steps">
            <div>
              1. Quét mã QR hoặc nhập khóa:
              <Typography.Text code copyable className="account__secret">
                {pending.secret}
              </Typography.Text>
            </div>
            <Form
              layout="vertical"
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
                label="2. Nhập mã 6 số để xác nhận"
                rules={[{ required: true, pattern: /^\d{6}$/, message: 'Mã gồm 6 chữ số' }]}
              >
                <Input
                  className="mono"
                  placeholder="000000"
                  maxLength={6}
                  inputMode="numeric"
                  style={{ width: 160 }}
                />
              </Form.Item>
              <Button type="primary" htmlType="submit" loading={enable.isPending}>
                Bật xác thực 2 lớp
              </Button>
            </Form>
          </div>
        </div>
      )}
    </section>
  );
}

export function MyAccountPage() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const initials = (user?.fullName ?? user?.userName ?? '?')
    .split(/\s+/)
    .filter(Boolean)
    .slice(-2)
    .map((w) => w[0]!.toUpperCase())
    .join('');
  return (
    <div className="account">
      <PageHeader title="Tài khoản & bảo mật" subtitle="Thông tin tài khoản, xác thực 2 lớp và mật khẩu" />
      <section className="account__profile">
        <span className="account__avatar" aria-hidden>
          {initials}
        </span>
        <InfoGrid
          items={[
            { label: 'Tên đăng nhập', value: user?.userName, mono: true },
            { label: 'Họ tên', value: user?.fullName },
            { label: 'Chức danh', value: user?.jobTitle },
            { label: 'Email', value: user?.email },
            ...(user?.supplierName ? [{ label: 'Nhà cung cấp', value: user.supplierName }] : []),
            ...(user?.departmentName ? [{ label: 'Khoa/phòng', value: user.departmentName }] : []),
            ...(user?.facilityName ? [{ label: 'Cơ sở', value: user.facilityName }] : []),
            {
              label: 'Vai trò',
              value: user?.roleNames.length ? (
                <span className="account__roles">
                  {user.roleNames.map((r) => (
                    <Tag key={r}>{r}</Tag>
                  ))}
                </span>
              ) : null,
            },
          ]}
        />
      </section>
      <section className="account__card">
        <div>
          <h2 className="account__title">Chữ ký hiển thị khi ký số</h2>
          <p className="account__lead">
            Ảnh được đặt vào ô chữ ký trên tệp PDF cùng họ tên và thời điểm ký. Nên dùng ảnh PNG nền trong
            suốt, chữ ký mực xanh hoặc đen, tối đa 2 MB.
          </p>
        </div>
        <div className="account__images">
          <SignatureImageSlot slot="signature" title="Chữ ký tay" hint="Dùng cho các bước Ký số." />
          <SignatureImageSlot
            slot="initial"
            title="Chữ ký nháy"
            hint="Dùng cho bước Ký nháy; để trống thì dùng chữ ký tay."
          />
        </div>
      </section>
      <MyCertificateCard />
      <div className="account__grid">
        <TwoFactorCard />
        <section className="account__card">
          <div>
            <h2 className="account__title">Đổi mật khẩu</h2>
            <p className="account__lead">
              Ít nhất 8 ký tự, có chữ hoa, chữ thường, chữ số và ký tự đặc biệt. Sau khi đổi, mọi phiên đăng
              nhập sẽ kết thúc.
            </p>
          </div>
          <ChangePasswordForm
            onDone={() => {
              authStore.clear();
              navigate('/dang-nhap', { replace: true });
            }}
          />
        </section>
      </div>
    </div>
  );
}
