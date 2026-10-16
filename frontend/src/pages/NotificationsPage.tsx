import { Button, Card, Empty, Segmented } from 'antd';
import { CheckCheck } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMarkNotificationsRead, useNotifications, type NotificationDto } from '../api/signing';
import { useAuth } from '../auth/useAuth';
import { PageHeader } from '../components/common';
import { NotificationIcon } from '../components/notificationStyle';
import { portalLink } from '../realtime/useRealtime';
import { formatDateTime } from '../utils/format';

/** Thông báo (design/He thong.dc.html): every recent notification, filter unread, mark read. */
export function NotificationsPage() {
  const { data, isLoading } = useNotifications(true);
  const markRead = useMarkNotificationsRead();
  const navigate = useNavigate();
  const { isSupplier } = useAuth();
  const [filter, setFilter] = useState<'all' | 'unread'>('all');
  const items = (data?.items ?? []).filter((n) => filter === 'all' || !n.isRead);

  const open = (n: NotificationDto) => {
    if (!n.isRead) markRead.mutate(n.id);
    const target = portalLink(n.link, isSupplier);
    if (target) navigate(target);
  };

  return (
    <>
      <PageHeader
        title="Thông báo"
        subtitle="Tài liệu chờ xử lý, kết quả ký, gọi hàng và cảnh báo của hệ thống"
        extra={
          <Button
            icon={<CheckCheck size={16} strokeWidth={1.75} aria-hidden />}
            disabled={!data?.unreadCount}
            onClick={() => markRead.mutate(undefined)}
          >
            Đánh dấu đã đọc tất cả
          </Button>
        }
      />
      <Card
        loading={isLoading}
        title={
          <Segmented
            value={filter}
            onChange={(v) => setFilter(v as 'all' | 'unread')}
            options={[
              { value: 'all', label: `Tất cả (${data?.items.length ?? 0})` },
              { value: 'unread', label: `Chưa đọc (${data?.unreadCount ?? 0})` },
            ]}
          />
        }
        styles={{ body: { padding: 0 } }}
      >
        {items.length === 0 ? (
          <Empty
            image={Empty.PRESENTED_IMAGE_SIMPLE}
            description="Không có thông báo"
            style={{ padding: 32 }}
          />
        ) : (
          items.map((n) => (
            <button
              type="button"
              key={n.id}
              className={`notification-item${n.isRead ? '' : ' notification-item--unread'}`}
              onClick={() => open(n)}
            >
              <NotificationIcon type={n.type} />
              <span className="notification-item__text">
                <span className="notification-item__title">{n.title}</span>
                {n.body && <span className="notification-item__body">{n.body}</span>}
                <span className="notification-item__time">{formatDateTime(n.createdAt)}</span>
              </span>
              {!n.isRead && <span className="notification-item__dot" aria-label="Chưa đọc" />}
            </button>
          ))
        )}
      </Card>
    </>
  );
}
