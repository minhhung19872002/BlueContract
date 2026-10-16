import { Badge, Button, Empty, List, Popover, Space, Typography } from 'antd';
import { BellOutlined, CheckOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { useMarkNotificationsRead, useNotifications, type NotificationDto } from '../api/signing';
import { formatDateTime } from '../utils/format';

/** Header bell: unread count + latest notifications; clicking one marks it read and opens its link. */
export function NotificationBell({ onOpen }: { onOpen: (link: string | null) => void }) {
  const [open, setOpen] = useState(false);
  const { data } = useNotifications(true);
  const markRead = useMarkNotificationsRead();

  const openItem = (n: NotificationDto) => {
    if (!n.isRead) markRead.mutate(n.id);
    setOpen(false);
    onOpen(n.link);
  };

  const content = (
    <div style={{ width: 'min(380px, calc(100vw - 32px))' }}>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }}>
        <Typography.Text strong>Thông báo</Typography.Text>
        <Button
          size="small"
          type="link"
          icon={<CheckOutlined />}
          disabled={!data?.unreadCount}
          onClick={() => markRead.mutate(undefined)}
        >
          Đánh dấu đã đọc tất cả
        </Button>
      </Space>
      {data?.items.length ? (
        <List
          size="small"
          style={{ maxHeight: 420, overflowY: 'auto' }}
          dataSource={data.items}
          renderItem={(n) => (
            <List.Item
              onClick={() => openItem(n)}
              style={{ cursor: 'pointer', background: n.isRead ? undefined : '#f0f6ff', paddingInline: 8 }}
            >
              <List.Item.Meta
                title={<Typography.Text strong={!n.isRead}>{n.title}</Typography.Text>}
                description={
                  <>
                    {n.body && <div style={{ whiteSpace: 'pre-line' }}>{n.body}</div>}
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {formatDateTime(n.createdAt)}
                    </Typography.Text>
                  </>
                }
              />
            </List.Item>
          )}
        />
      ) : (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có thông báo" />
      )}
    </div>
  );

  return (
    <Popover content={content} trigger="click" open={open} onOpenChange={setOpen} placement="bottomRight">
      <Badge count={data?.unreadCount ?? 0} size="small" overflowCount={99}>
        <Button
          type="text"
          shape="circle"
          icon={<BellOutlined style={{ fontSize: 18 }} />}
          aria-label="Thông báo"
        />
      </Badge>
    </Popover>
  );
}
