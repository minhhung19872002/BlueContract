import { Alert, Card, Col, Descriptions, Row, Space, Spin, Tag, Typography } from 'antd';
import { useReadiness, useSystemInfo } from '../api/system';
import type { ApiProblem } from '../api/http';
import { useAuth } from '../auth/useAuth';
import { P } from '../auth/permissions';
import { PageHeader } from '../components/common';
import { formatDate, formatDateTime } from '../utils/format';

const SERVICE_LABELS: Record<string, string> = {
  postgres: 'Cơ sở dữ liệu (PostgreSQL)',
  minio: 'Lưu trữ tệp (MinIO)',
  gotenberg: 'Chuyển đổi PDF (Gotenberg)',
};

function SystemStatus() {
  const info = useSystemInfo();
  const ready = useReadiness();
  return (
    <Row gutter={[16, 16]}>
      <Col xs={24} lg={12}>
        <Card title="Thông tin hệ thống">
          {info.isPending && <Spin />}
          {info.isError && (
            <Alert type="error" showIcon message={(info.error as unknown as ApiProblem).title} />
          )}
          {info.data && (
            <Descriptions column={1} size="small">
              <Descriptions.Item label="Phiên bản">{info.data.version}</Descriptions.Item>
              <Descriptions.Item label="Môi trường">{info.data.environment}</Descriptions.Item>
              <Descriptions.Item label="Ngày làm việc">{formatDate(info.data.todayLocal)}</Descriptions.Item>
              <Descriptions.Item label="Giờ máy chủ">
                {formatDateTime(info.data.serverTimeUtc)}
              </Descriptions.Item>
            </Descriptions>
          )}
        </Card>
      </Col>
      <Col xs={24} lg={12}>
        <Card title="Tình trạng dịch vụ">
          {ready.isPending && <Spin />}
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
      </Col>
    </Row>
  );
}

/** Internal dashboard. Business widgets (documents waiting, contracts expiring...) arrive with their modules. */
export function HomePage() {
  const { user, can } = useAuth();
  return (
    <>
      <PageHeader
        title={`Xin chào, ${user?.fullName ?? ''}`}
        subtitle={[user?.jobTitle, user?.departmentName].filter(Boolean).join(' · ')}
      />
      {can(P.SettingManage, P.AuditView) ? (
        <SystemStatus />
      ) : (
        <Card>
          <Typography.Paragraph>Chọn chức năng ở menu bên trái để bắt đầu làm việc.</Typography.Paragraph>
        </Card>
      )}
    </>
  );
}

export function SupplierHomePage() {
  const { user } = useAuth();
  return (
    <>
      <PageHeader title={`Xin chào, ${user?.fullName ?? ''}`} subtitle={user?.supplierName ?? undefined} />
      <Card>
        <Typography.Paragraph>
          Đây là Cổng Nhà cung cấp. Bạn có thể xem thông tin công ty và quản lý tài khoản nhân viên tại mục{' '}
          <b>Tài khoản công ty</b>.
        </Typography.Paragraph>
      </Card>
    </>
  );
}
