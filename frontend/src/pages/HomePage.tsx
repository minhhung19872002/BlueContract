import { Alert, Card, Descriptions, Space, Spin, Tag, Typography } from 'antd';
import { useReadiness, useSystemInfo } from '../api/system';
import type { ApiProblem } from '../api/http';
import { formatDate, formatDateTime } from '../utils/format';

const SERVICE_LABELS: Record<string, string> = {
  postgres: 'Cơ sở dữ liệu (PostgreSQL)',
  minio: 'Lưu trữ tệp (MinIO)',
  gotenberg: 'Chuyển đổi PDF (Gotenberg)',
};

export function HomePage() {
  const info = useSystemInfo();
  const ready = useReadiness();

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Typography.Title level={3} style={{ margin: 0 }}>
        Tổng quan
      </Typography.Title>

      <Card title="Thông tin hệ thống">
        {info.isPending && <Spin />}
        {info.isError && (
          <Alert type="error" showIcon message={(info.error as unknown as ApiProblem).title} />
        )}
        {info.data && (
          <Descriptions column={{ xs: 1, md: 2 }} size="small">
            <Descriptions.Item label="Ứng dụng">{info.data.name}</Descriptions.Item>
            <Descriptions.Item label="Phiên bản">{info.data.version}</Descriptions.Item>
            <Descriptions.Item label="Môi trường">{info.data.environment}</Descriptions.Item>
            <Descriptions.Item label="Ngày làm việc">{formatDate(info.data.todayLocal)}</Descriptions.Item>
            <Descriptions.Item label="Giờ máy chủ">
              {formatDateTime(info.data.serverTimeUtc)}
            </Descriptions.Item>
          </Descriptions>
        )}
      </Card>

      <Card title="Tình trạng dịch vụ">
        {ready.isPending && <Spin />}
        {ready.isError && (
          <Alert type="error" showIcon message={(ready.error as unknown as ApiProblem).title} />
        )}
        {ready.data && (
          <Space direction="vertical">
            {Object.entries(ready.data.checks).map(([name, check]) => (
              <Space key={name}>
                <Tag
                  color={
                    check.status === 'Healthy' ? 'green' : check.status === 'Degraded' ? 'orange' : 'red'
                  }
                >
                  {check.status === 'Healthy'
                    ? 'Hoạt động'
                    : check.status === 'Degraded'
                      ? 'Suy giảm'
                      : 'Lỗi'}
                </Tag>
                <span>{SERVICE_LABELS[name] ?? name}</span>
              </Space>
            ))}
          </Space>
        )}
      </Card>
    </Space>
  );
}
