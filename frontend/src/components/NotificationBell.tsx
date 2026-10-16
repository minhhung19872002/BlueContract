import { Empty, Popover } from 'antd';
import { Bell, CheckCheck } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMarkNotificationsRead, useNotifications, type NotificationDto } from '../api/signing';
import { useAuth } from '../auth/useAuth';
import { formatDateTime } from '../utils/format';
import { NotificationIcon } from './notificationStyle';

/** Header bell (design/App Shell.dc.html): unread count, latest notifications, mark all read, link to the full list. */
export function NotificationBell({ onOpen }: { onOpen: (link: string | null) => void }) {
  const [open, setOpen] = useState(false);
  const { data } = useNotifications(true);
  const markRead = useMarkNotificationsRead();
  const navigate = useNavigate();
  const { isSupplier } = useAuth();
  const unread = data?.unreadCount ?? 0;

  const openItem = (n: NotificationDto) => {
    if (!n.isRead) markRead.mutate(n.id);
    setOpen(false);
    onOpen(n.link);
  };

  const content = (
    <div className="notification-panel">
      <div className="notification-panel__head">
        <span className="notification-panel__title">Thông báo</span>
        <button
          type="button"
          className="notification-panel__action"
          disabled={!unread}
          onClick={() => markRead.mutate(undefined)}
        >
          <CheckCheck size={15} strokeWidth={1.75} aria-hidden />
          Đánh dấu đã đọc tất cả
        </button>
      </div>
      <div className="notification-panel__list">
        {data?.items.length ? (
          data.items.slice(0, 10).map((n) => (
            <button
              type="button"
              key={n.id}
              className={`notification-item${n.isRead ? '' : ' notification-item--unread'}`}
              onClick={() => openItem(n)}
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
        ) : (
          <Empty
            image={Empty.PRESENTED_IMAGE_SIMPLE}
            description="Chưa có thông báo"
            style={{ padding: 16 }}
          />
        )}
      </div>
      <button
        type="button"
        className="notification-panel__footer"
        onClick={() => {
          setOpen(false);
          navigate(isSupplier ? '/ncc/thong-bao' : '/thong-bao');
        }}
      >
        Xem tất cả thông báo
      </button>
    </div>
  );

  return (
    <Popover
      content={content}
      trigger="click"
      open={open}
      onOpenChange={setOpen}
      placement="bottomRight"
      arrow={false}
      styles={{ body: { padding: 0 } }}
    >
      <button
        type="button"
        className="app-header__icon-btn app-header__bell"
        aria-label={`Thông báo${unread ? `, ${unread} chưa đọc` : ''}`}
      >
        <Bell size={18} strokeWidth={1.75} aria-hidden />
        {unread > 0 && <span className="app-header__bell-count">{unread > 99 ? '99+' : unread}</span>}
      </button>
    </Popover>
  );
}
