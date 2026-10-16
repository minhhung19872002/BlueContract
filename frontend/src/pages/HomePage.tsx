import {
  Alert,
  Card,
  Col,
  Descriptions,
  Empty,
  List,
  Row,
  Space,
  Spin,
  Statistic,
  Table,
  Tag,
  Typography,
} from 'antd';
import { Link, useNavigate } from 'react-router-dom';
import { useReadiness, useSystemInfo } from '../api/system';
import { useInternalDashboard, useSupplierDashboard } from '../api/reports';
import type { ApiProblem } from '../api/http';
import { useAuth } from '../auth/useAuth';
import { P } from '../auth/permissions';
import { BarChart } from '../components/BarChart';
import { StatusTag } from '../components/StatusTag';
import { PageHeader } from '../components/common';
import { formatDate, formatDateTime, formatMoney, formatQuantity } from '../utils/format';

const SERVICE_LABELS: Record<string, string> = {
  postgres: 'Cơ sở dữ liệu (PostgreSQL)',
  storage: 'Lưu trữ tệp (S3)',
  gotenberg: 'Chuyển đổi PDF (Gotenberg)',
};

const PAYMENT_SERIES = [
  { key: 'paid', label: 'Đã thanh toán', color: '#52c41a' },
  { key: 'unpaid', label: 'Chưa thanh toán', color: '#fa8c16' },
];

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

function StatCard({
  title,
  value,
  suffix,
  to,
  danger,
}: {
  title: string;
  value: number | string;
  suffix?: string;
  to?: string;
  danger?: boolean;
}) {
  const navigate = useNavigate();
  return (
    <Card hoverable={!!to} onClick={to ? () => navigate(to) : undefined} style={{ height: '100%' }}>
      <Statistic
        title={title}
        value={value}
        suffix={suffix}
        valueStyle={danger ? { color: '#cf1322' } : undefined}
      />
    </Card>
  );
}

/** Tổng quan nội bộ (spec §9.1-1). */
export function HomePage() {
  const { user, can } = useAuth();
  const dashboard = useInternalDashboard();
  const d = dashboard.data;
  return (
    <>
      <PageHeader
        title={`Xin chào, ${user?.fullName ?? ''}`}
        subtitle={[user?.jobTitle, user?.departmentName].filter(Boolean).join(' · ')}
      />
      {!d ? (
        <Card loading={dashboard.isLoading} />
      ) : (
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          <Row gutter={[16, 16]}>
            <Col xs={12} lg={6}>
              <StatCard
                title="Tài liệu chờ tôi xử lý"
                value={d.pendingTotal}
                suffix={d.pendingOverdue > 0 ? `(${d.pendingOverdue} quá hạn)` : undefined}
                to="/trinh-ky"
                danger={d.pendingOverdue > 0}
              />
            </Col>
            <Col xs={12} lg={6}>
              <StatCard
                title="Phiếu nhập kho cảnh báo / không hợp lệ"
                value={`${d.warningReceipts} / ${d.invalidReceipts}`}
                to="/nhap-kho?validation=Warning"
                danger={d.invalidReceipts > 0}
              />
            </Col>
            <Col xs={12} lg={6}>
              <StatCard
                title="Gọi hàng chờ NCC xác nhận (quá hạn)"
                value={`${d.pendingCallOrders} (${d.overdueCallOrders})`}
                to="/goi-hang?overdue=true"
                danger={d.overdueCallOrders > 0}
              />
            </Col>
            <Col xs={12} lg={6}>
              <StatCard
                title="Đã thanh toán / còn phải trả"
                value={`${formatMoney(d.paidTotal)} / ${formatMoney(d.unpaidTotal)}`}
                to="/thanh-toan?tab=valuation"
              />
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} xl={8}>
              <Card title="Chờ tôi xử lý theo loại" style={{ height: '100%' }}>
                {d.pendingByType.length === 0 ? (
                  <Empty description="Không có tài liệu chờ xử lý" />
                ) : (
                  <List
                    dataSource={d.pendingByType}
                    renderItem={(p) => (
                      <List.Item
                        actions={[
                          <Link key="go" to="/trinh-ky">
                            {p.count} tài liệu
                          </Link>,
                        ]}
                      >
                        <Space>
                          <StatusTag domain="documentType" value={p.type} />
                          {p.overdue > 0 && (
                            <Typography.Text type="danger">{p.overdue} quá hạn</Typography.Text>
                          )}
                        </Space>
                      </List.Item>
                    )}
                  />
                )}
              </Card>
            </Col>
            <Col xs={24} xl={16}>
              <Card title="Giá trị thanh toán 12 tháng gần nhất" style={{ height: '100%' }}>
                <BarChart data={d.payments} category="month" series={PAYMENT_SERIES} />
              </Card>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} xl={12}>
              <Card title={`Hợp đồng sắp hết hạn (≤ ${d.expiryWarningDays} ngày)`}>
                <Table
                  rowKey="id"
                  size="small"
                  pagination={false}
                  dataSource={d.expiringContracts}
                  locale={{ emptyText: 'Không có' }}
                  columns={[
                    {
                      title: 'Hợp đồng',
                      dataIndex: 'number',
                      render: (v: string, r) => <Link to={`/hop-dong/${r.id}`}>{v}</Link>,
                    },
                    { title: 'Nhà cung cấp', dataIndex: 'supplierName' },
                    { title: 'Hết hạn', dataIndex: 'expiryDate', render: (v: string) => formatDate(v) },
                    {
                      title: 'Còn',
                      dataIndex: 'daysLeft',
                      align: 'right',
                      render: (v: number) => (
                        <Typography.Text type={v < 0 ? 'danger' : 'warning'}>
                          {v < 0 ? 'Đã hết hạn' : `${v} ngày`}
                        </Typography.Text>
                      ),
                    },
                  ]}
                />
              </Card>
            </Col>
            <Col xs={24} xl={12}>
              <Card title={`Mặt hàng sắp hết số lượng hợp đồng (≤ ${d.lowQuantityPercent}%)`}>
                <Table
                  rowKey={(r) => `${r.contractId}-${r.itemName}`}
                  size="small"
                  pagination={false}
                  dataSource={d.lowQuantityItems}
                  locale={{ emptyText: 'Không có' }}
                  columns={[
                    {
                      title: 'Hợp đồng',
                      dataIndex: 'contractNumber',
                      render: (v: string, r) => <Link to={`/hop-dong/${r.contractId}`}>{v}</Link>,
                    },
                    { title: 'Mặt hàng', dataIndex: 'itemName' },
                    {
                      title: 'Còn lại',
                      key: 'remaining',
                      align: 'right',
                      render: (_, r) =>
                        `${formatQuantity(r.remaining)} / ${formatQuantity(r.quantity)} ${r.unit} (${r.percent}%)`,
                    },
                  ]}
                />
              </Card>
            </Col>
          </Row>
          {can(P.SettingManage, P.AuditView) && <SystemStatus />}
        </Space>
      )}
    </>
  );
}

/** Tổng quan Cổng NCC (spec §9.2-1). */
export function SupplierHomePage() {
  const { user } = useAuth();
  const dashboard = useSupplierDashboard();
  const d = dashboard.data;
  return (
    <>
      <PageHeader title={`Xin chào, ${user?.fullName ?? ''}`} subtitle={user?.supplierName ?? undefined} />
      {!d ? (
        <Card loading={dashboard.isLoading} />
      ) : (
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          <Row gutter={[16, 16]}>
            <Col xs={12} lg={6}>
              <StatCard title="Tài liệu chờ ký" value={d.pendingSigning} to="/ncc/trinh-ky" />
            </Col>
            <Col xs={12} lg={6}>
              <StatCard
                title="Đơn gọi hàng chờ xác nhận (quá hạn)"
                value={`${d.callOrdersAwaiting} (${d.callOrdersOverdue})`}
                to="/ncc/goi-hang"
                danger={d.callOrdersOverdue > 0}
              />
            </Col>
            <Col xs={12} lg={6}>
              <StatCard title="Hợp đồng đang hiệu lực" value={d.effectiveContracts} to="/ncc/hop-dong" />
            </Col>
            <Col xs={12} lg={6}>
              <StatCard
                title="Phiếu nhập kho cần bổ sung hóa đơn"
                value={d.receiptsWithoutInvoice}
                to="/ncc/nhap-kho?hasInvoice=false"
                danger={d.receiptsWithoutInvoice > 0}
              />
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} xl={8}>
              <Card title="Công nợ bệnh viện còn phải trả" style={{ height: '100%' }}>
                <Space direction="vertical" size={16}>
                  <Statistic
                    title="Đã xác định giá trị (08a), chưa thanh toán"
                    value={formatMoney(d.valuedUnpaid)}
                    valueStyle={{ color: '#fa8c16' }}
                  />
                  <Statistic title="Đã nghiệm thu, chưa lập 08a" value={formatMoney(d.acceptedNotValued)} />
                  <Statistic
                    title="Đã được thanh toán"
                    value={formatMoney(d.paidTotal)}
                    valueStyle={{ color: '#389e0d' }}
                  />
                </Space>
              </Card>
            </Col>
            <Col xs={24} xl={16}>
              <Card title="Thanh toán 12 tháng gần nhất" style={{ height: '100%' }}>
                <BarChart data={d.payments} category="month" series={PAYMENT_SERIES} />
              </Card>
            </Col>
          </Row>
        </Space>
      )}
    </>
  );
}
