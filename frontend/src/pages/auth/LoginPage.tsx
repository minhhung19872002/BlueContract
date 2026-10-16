import { Alert, Button, Form, Input, Modal, Segmented } from 'antd';
import {
  ArrowLeft,
  ArrowLeftRight,
  ArrowRight,
  Building2,
  Hospital,
  LockKeyhole,
  Moon,
  PenLine,
  ShieldCheck,
  Smartphone,
  Sun,
  UserRound,
} from 'lucide-react';
import { useState } from 'react';
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import { useLogin, useLoginTwoFactor } from '../../api/auth';
import { toApiProblem } from '../../api/http';
import { useAuth } from '../../auth/useAuth';
import { useThemeMode } from '../../hooks/useThemeMode';
import './auth.css';

type Portal = 'internal' | 'supplier';

const FEATURES = [
  {
    icon: PenLine,
    tone: 'blue',
    title: 'Ký số toàn trình',
    text: 'Hợp đồng, phiếu nhập kho, nghiệm thu, thanh lý — ký và đóng dấu điện tử nhiều bước.',
  },
  {
    icon: ArrowLeftRight,
    tone: 'teal',
    title: 'Liên thông HIS',
    text: 'Phiếu nhập kho từ HIS đẩy sang tự động, đối chiếu với hợp đồng và hóa đơn.',
  },
  {
    icon: ShieldCheck,
    tone: 'blue',
    title: 'Lưu trữ toàn vẹn SHA-256',
    text: 'Hồ sơ điện tử theo hợp đồng, kiểm tra toàn vẹn hằng ngày.',
  },
] as const;

/** Shared frame of the sign-in pages (design/Dang nhap.dc.html): brand panel on the left from 960px, form on the right. */
export function AuthFrame({ children }: { children: React.ReactNode }) {
  const { mode, toggle } = useThemeMode();
  return (
    <div className="auth">
      <aside className="auth__brand" aria-hidden={false}>
        <div className="auth__grid" aria-hidden />
        <div className="auth__ring auth__ring--outer" aria-hidden />
        <div className="auth__ring auth__ring--inner" aria-hidden />
        <div className="auth__diamond" aria-hidden />
        <div className="auth__line" aria-hidden />
        <div className="auth__logo">
          <img src="/favicon.svg" width={40} height={40} alt="" />
          <div>
            <div className="auth__logo-name">BlueProcure</div>
            <div className="auth__logo-org">Cổng mua sắm bệnh viện</div>
          </div>
        </div>
        <div className="auth__pitch">
          <h1>Cổng Quản lý &amp; Thanh toán Mua sắm Hàng hóa Trực tuyến</h1>
          <div className="auth__features">
            {FEATURES.map(({ icon: Icon, tone, title, text }) => (
              <div key={title} className="auth__feature">
                <span className={`auth__feature-icon auth__feature-icon--${tone}`}>
                  <Icon size={20} strokeWidth={1.75} aria-hidden />
                </span>
                <div>
                  <div className="auth__feature-title">{title}</div>
                  <div className="auth__feature-text">{text}</div>
                </div>
              </div>
            ))}
          </div>
        </div>
        <div className="auth__brand-foot">
          <span>Phiên bản {import.meta.env.VITE_APP_VERSION ?? '1.0.0'}</span>
          <span>Hỗ trợ: Phòng Công nghệ thông tin của bệnh viện</span>
        </div>
      </aside>
      <main className="auth__main">
        <div className="auth__top">
          <div className="auth__mini-brand">
            <img src="/favicon.svg" width={32} height={32} alt="" />
            <span>BlueProcure</span>
          </div>
          <div style={{ flex: 1 }} />
          <button
            type="button"
            className="auth__theme"
            onClick={toggle}
            aria-label="Đổi giao diện sáng / tối"
          >
            {mode === 'dark' ? (
              <Sun size={17} strokeWidth={1.75} aria-hidden />
            ) : (
              <Moon size={17} strokeWidth={1.75} aria-hidden />
            )}
          </button>
        </div>
        <div className="auth__center">
          <div className="auth__panel">{children}</div>
        </div>
        <div className="auth__foot">
          <span>© {new Date().getFullYear()} BlueProcure</span>
        </div>
      </main>
    </div>
  );
}

export function LoginPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const { isAuthenticated } = useAuth();
  const login = useLogin();
  const twoFactor = useLoginTwoFactor();
  const [portal, setPortal] = useState<Portal>('internal');
  const [challenge, setChallenge] = useState<string | null>(null);
  const [userShown, setUserShown] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [code, setCode] = useState('');

  const returnUrl = params.get('returnUrl');
  const safeReturn = returnUrl && returnUrl.startsWith('/') && !returnUrl.startsWith('//') ? returnUrl : '/';

  if (isAuthenticated) return <Navigate to={safeReturn} replace />;

  const done = (mustChange?: boolean) =>
    navigate(mustChange ? '/doi-mat-khau' : safeReturn, { replace: true });

  const verify = async (value: string) => {
    if (!challenge || !/^\d{6}$/.test(value)) return;
    setError(null);
    try {
      const result = await twoFactor.mutateAsync({ challenge, code: value });
      done(result.user?.mustChangePassword);
    } catch (e) {
      const problem = toApiProblem(e);
      setError(problem.title);
      setCode('');
      if (problem.code === 'TWO_FACTOR_CHALLENGE_EXPIRED' || problem.code === 'ACCOUNT_LOCKED')
        setChallenge(null);
    }
  };

  return (
    <AuthFrame>
      {!challenge ? (
        <>
          <div>
            <h2 className="auth__title">Đăng nhập</h2>
            <p className="auth__lead">Sử dụng tài khoản được bệnh viện cấp.</p>
          </div>
          <Segmented<Portal>
            block
            size="large"
            className="auth__portals"
            aria-label="Cổng đăng nhập"
            value={portal}
            onChange={setPortal}
            options={[
              {
                value: 'internal',
                label: 'Nội bộ bệnh viện',
                icon: <Hospital size={16} strokeWidth={1.75} aria-hidden />,
              },
              {
                value: 'supplier',
                label: 'Nhà cung cấp',
                icon: <Building2 size={16} strokeWidth={1.75} aria-hidden />,
              },
            ]}
          />
          {error && <Alert type="error" showIcon message={error} />}
          <Form
            layout="vertical"
            requiredMark={false}
            className="auth__form"
            onFinish={async (values: { userName: string; password: string }) => {
              setError(null);
              try {
                const result = await login.mutateAsync(values);
                if (result.requiresTwoFactor) {
                  setUserShown(values.userName);
                  setCode('');
                  setChallenge(result.twoFactorChallenge ?? null);
                } else done(result.user?.mustChangePassword);
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
              <Input
                size="large"
                prefix={<UserRound size={17} strokeWidth={1.75} aria-hidden />}
                autoComplete="username"
                autoFocus
                placeholder={portal === 'supplier' ? 'vd: ncc1' : 'vd: muasam'}
              />
            </Form.Item>
            <Form.Item
              name="password"
              label="Mật khẩu"
              rules={[{ required: true, message: 'Vui lòng nhập mật khẩu' }]}
            >
              <Input.Password
                size="large"
                prefix={<LockKeyhole size={17} strokeWidth={1.75} aria-hidden />}
                autoComplete="current-password"
                placeholder="••••••••"
              />
            </Form.Item>
            <div className="auth__row">
              <button
                type="button"
                className="auth__link"
                onClick={() =>
                  Modal.info({
                    title: 'Quên mật khẩu',
                    content:
                      portal === 'supplier'
                        ? 'Quản trị viên của công ty (tài khoản Quản trị NCC) hoặc Tổ Mua sắm của bệnh viện đặt lại mật khẩu cho anh/chị. Mật khẩu tạm thời phải đổi ở lần đăng nhập kế tiếp.'
                        : 'Liên hệ quản trị hệ thống (Phòng Công nghệ thông tin) để được đặt lại mật khẩu. Mật khẩu tạm thời phải đổi ở lần đăng nhập kế tiếp.',
                    okText: 'Đã hiểu',
                  })
                }
              >
                Quên mật khẩu?
              </button>
            </div>
            <Button
              type="primary"
              htmlType="submit"
              block
              size="large"
              loading={login.isPending}
              className="auth__submit"
            >
              Đăng nhập
              <ArrowRight size={17} strokeWidth={1.75} aria-hidden />
            </Button>
          </Form>
          {portal === 'supplier' && (
            <p className="auth__note">
              Nhà cung cấp chưa có tài khoản? Liên hệ Tổ Mua sắm để được cấp tài khoản Quản trị NCC.
            </p>
          )}
        </>
      ) : (
        <>
          <span className="auth__badge">
            <Smartphone size={22} strokeWidth={1.75} aria-hidden />
          </span>
          <div>
            <h2 className="auth__title">Xác thực 2 lớp</h2>
            <p className="auth__lead">
              Nhập mã 6 chữ số trong ứng dụng xác thực (Google/Microsoft Authenticator) của tài khoản{' '}
              <strong className="mono">{userShown}</strong>.
            </p>
          </div>
          {error && <Alert type="error" showIcon message={error} />}
          <div className="auth__otp">
            <span className="auth__otp-label" id="otp-label">
              Mã xác thực
            </span>
            <Input.OTP
              length={6}
              size="large"
              autoFocus
              value={code}
              aria-labelledby="otp-label"
              formatter={(v) => v.replace(/\D/g, '')}
              onChange={(v) => {
                setCode(v);
                void verify(v);
              }}
            />
            <span className="auth__hint">Gõ hoặc dán mã — mã đổi sau mỗi 30 giây.</span>
          </div>
          <Button
            type="primary"
            block
            size="large"
            loading={twoFactor.isPending}
            disabled={code.length !== 6}
            onClick={() => void verify(code)}
          >
            Xác nhận
          </Button>
          <Button
            type="link"
            block
            icon={<ArrowLeft size={16} aria-hidden />}
            onClick={() => setChallenge(null)}
          >
            Quay lại
          </Button>
        </>
      )}
    </AuthFrame>
  );
}
