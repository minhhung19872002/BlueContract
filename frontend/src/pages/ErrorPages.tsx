import { Button } from 'antd';
import { House, Lock, MapPinOff, RefreshCw, Search, ServerCrash, type LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { isRouteErrorResponse, useNavigate, useRouteError } from 'react-router-dom';
import { useAuth } from '../auth/useAuth';
import type { Tone } from '../components/StatusTag';
import dayjs, { APP_TIME_ZONE } from '../i18n/dayjs';
import './errorPages.css';

/** Full-height error state of design/He thong.dc.html: big mono code, icon, title, text, actions. */
export function ErrorState({
  code,
  tone,
  icon: Icon,
  title,
  body,
  reference,
  secondary,
}: {
  code: string;
  tone: Tone;
  icon: LucideIcon;
  title: string;
  body: ReactNode;
  reference?: string;
  secondary?: ReactNode;
}) {
  const navigate = useNavigate();
  const { isSupplier, isAuthenticated } = useAuth();
  return (
    <section className="error-state">
      <span className="error-state__code mono" style={{ color: `var(--bp-${tone}-fg)` }}>
        {code}
      </span>
      <span
        className="error-state__icon"
        style={{ background: `var(--bp-${tone}-bg)`, color: `var(--bp-${tone}-fg)` }}
        aria-hidden
      >
        <Icon size={24} strokeWidth={1.75} />
      </span>
      <h1 className="error-state__title">{title}</h1>
      <p className="error-state__body">{body}</p>
      {reference && <span className="error-state__ref mono">{reference}</span>}
      <div className="error-state__actions">
        <Button
          type="primary"
          icon={<House size={16} aria-hidden />}
          onClick={() => navigate(!isAuthenticated ? '/dang-nhap' : isSupplier ? '/ncc' : '/tong-quan')}
        >
          {isAuthenticated ? 'Về Tổng quan' : 'Đăng nhập'}
        </Button>
        {secondary}
      </div>
    </section>
  );
}

export function NotFoundPage() {
  return (
    <ErrorState
      code="404"
      tone="primary"
      icon={MapPinOff}
      title="Không tìm thấy trang"
      body="Đường dẫn không tồn tại hoặc tài liệu đã bị xóa. Kiểm tra lại địa chỉ hoặc tìm bằng Ctrl+K."
      secondary={
        <Button
          icon={<Search size={16} aria-hidden />}
          onClick={() => window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }))}
        >
          Tìm kiếm
        </Button>
      }
    />
  );
}

export function ForbiddenPage() {
  return (
    <ErrorState
      code="403"
      tone="warning"
      icon={Lock}
      title="Không có quyền truy cập"
      body="Tài khoản của anh/chị chưa được cấp quyền xem trang này. Liên hệ Quản trị hệ thống nếu cần quyền."
    />
  );
}

/** Router error element: a render or loader failure shows the 500 state inside the shell, with a reference. */
export function RouteErrorPage() {
  const error = useRouteError();
  if (isRouteErrorResponse(error) && error.status === 404) return <NotFoundPage />;
  if (isRouteErrorResponse(error) && error.status === 403) return <ForbiddenPage />;
  const detail = error instanceof Error ? error.message : isRouteErrorResponse(error) ? error.statusText : '';
  return (
    <ErrorState
      code="500"
      tone="danger"
      icon={ServerCrash}
      title="Lỗi hệ thống"
      body="Trang gặp sự cố khi hiển thị. Dữ liệu chưa lưu có thể chưa được ghi nhận — vui lòng tải lại trang hoặc thử lại sau ít phút."
      reference={`Mã tham chiếu: ${dayjs().tz(APP_TIME_ZONE).format('DD/MM/YYYY HH:mm:ss')}${detail ? ` · ${detail.slice(0, 120)}` : ''}`}
      secondary={
        <Button icon={<RefreshCw size={16} aria-hidden />} onClick={() => window.location.reload()}>
          Thử lại
        </Button>
      }
    />
  );
}
