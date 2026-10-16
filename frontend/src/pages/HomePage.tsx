import { Alert, Button, Card, Empty, Spin, Table, Typography } from 'antd';
import {
  ArrowRight,
  Building2,
  CircleAlert,
  FileClock,
  FileSignature,
  FileWarning,
  PackageCheck,
  Receipt,
  Truck,
  Wallet,
} from 'lucide-react';
import { Link, useNavigate } from 'react-router-dom';
import { useReadiness, useSystemInfo } from '../api/system';
import { useInternalDashboard, useSupplierDashboard } from '../api/reports';
import type { ApiProblem } from '../api/http';
import { useAuth } from '../auth/useAuth';
import { P } from '../auth/permissions';
import { BarChart } from '../components/BarChart';
import { StatusTag } from '../components/StatusTag';
import { PageHeader } from '../components/common';
import { InfoGrid, Panel } from '../components/layout';
import { KpiCard, KpiStrip } from '../components/patterns';
import { formatDate, formatDateTime, formatMoney, formatQuantity } from '../utils/format';
import './home.css';

const SERVICE_LABELS: Record<string, string> = {
  postgres: 'Cơ sở dữ liệu (PostgreSQL)',
  storage: 'Lưu trữ tệp (S3)',
  gotenberg: 'Chuyển đổi PDF (Gotenberg)',
};

const PAYMENT_SERIES = [
  { key: 'paid', label: 'Đã thanh toán', color: 'var(--bp-success-solid)' },
  { key: 'unpaid', label: 'Chưa thanh toán', color: 'var(--bp-orange-solid)' },
];

const ICON = { size: 20, strokeWidth: 1.75, 'aria-hidden': true } as const;

function SeeAll({ to }: { to: string }) {
  return (
    <Link to={to} className="panel__link">
      Xem tất cả
      <ArrowRight size={14} aria-hidden />
    </Link>
  );
}

function SystemStatus() {
  const info = useSystemInfo();
  const ready = useReadiness();
  return (
    <div className="home-grid home-grid--halves">
      <Panel title="Thông tin hệ thống">
        {info.isPending && <Spin />}
        {info.isError && (
          <Alert type="error" showIcon message={(info.error as unknown as ApiProblem).title} />
        )}
        {info.data && (
          <InfoGrid
            minColumn={160}
            items={[
              { label: 'Phiên bản', value: info.data.version, mono: true },
              { label: 'Môi trường', value: info.data.environment },
              { label: 'Ngày làm việc', value: formatDate(info.data.todayLocal) },
              { label: 'Giờ máy chủ', value: formatDateTime(info.data.serverTimeUtc) },
            ]}
          />
        )}
      </Panel>
      <Panel title="Tình trạng dịch vụ">
        {ready.isPending && <Spin />}
        {ready.data && (
          <ul className="home-services">
            {Object.entries(ready.data.checks).map(([name, check]) => {
              const tone =
                check.status === 'Healthy' ? 'success' : check.status === 'Degraded' ? 'warning' : 'danger';
              return (
                <li key={name}>
                  <span className="home-services__dot" style={{ background: `var(--bp-${tone}-solid)` }} />
                  <span className="home-services__name">{SERVICE_LABELS[name] ?? name}</span>
                  <span style={{ color: `var(--bp-${tone}-fg)` }}>
                    {check.status === 'Healthy'
                      ? 'Hoạt động'
                      : check.status === 'Degraded'
                        ? 'Suy giảm'
                        : 'Lỗi'}
                  </span>
                </li>
              );
            })}
          </ul>
        )}
      </Panel>
    </div>
  );
}

function PaymentPanel({
  data,
  title,
}: {
  data: { month: string; paid: number; unpaid: number }[];
  title: string;
}) {
  const total = data.reduce((s, d) => s + d.paid + d.unpaid, 0);
  return (
    <Panel
      title={title}
      extra={
        <span className="panel__meta">
          Tổng: <strong>{formatMoney(total)}</strong>
        </span>
      }
    >
      <BarChart data={data} category="month" series={PAYMENT_SERIES} stacked label={title} />
    </Panel>
  );
}

/** Tổng quan nội bộ (spec §9.1-1, design/Tong quan.dc.html). */
export function HomePage() {
  const { user, can } = useAuth();
  const navigate = useNavigate();
  const dashboard = useInternalDashboard();
  const d = dashboard.data;
  const debt = d ? d.paidTotal + d.unpaidTotal : 0;
  return (
    <>
      <PageHeader
        title={`Xin chào, ${user?.fullName ?? ''}`}
        subtitle={[user?.jobTitle, user?.departmentName].filter(Boolean).join(' · ')}
      />
      {!d ? (
        <Card loading={dashboard.isLoading} />
      ) : (
        <div className="home">
          <KpiStrip>
            <KpiCard
              icon={<FileSignature {...ICON} />}
              label="Tài liệu chờ tôi xử lý"
              value={d.pendingTotal}
              tone={d.pendingOverdue > 0 ? 'danger' : 'primary'}
              sub={
                d.pendingOverdue > 0 ? `${d.pendingOverdue} tài liệu quá hạn` : 'Không có tài liệu quá hạn'
              }
              onClick={() => navigate('/trinh-ky')}
            />
            <KpiCard
              icon={<FileWarning {...ICON} />}
              label="Phiếu nhập kho cảnh báo / không hợp lệ"
              value={`${d.warningReceipts} / ${d.invalidReceipts}`}
              tone={d.invalidReceipts > 0 ? 'danger' : d.warningReceipts > 0 ? 'warning' : 'success'}
              sub="Đối chiếu với hợp đồng và hóa đơn"
              onClick={() => navigate('/nhap-kho?validation=Warning')}
            />
            <KpiCard
              icon={<Truck {...ICON} />}
              label="Gọi hàng chờ NCC xác nhận"
              value={d.pendingCallOrders}
              tone={d.overdueCallOrders > 0 ? 'orange' : 'teal'}
              sub={
                d.overdueCallOrders > 0
                  ? `${d.overdueCallOrders} đơn quá hạn xác nhận`
                  : 'Không có đơn quá hạn'
              }
              onClick={() => navigate('/goi-hang?overdue=true')}
            />
            <KpiCard
              icon={<Wallet {...ICON} />}
              label="Còn phải trả nhà cung cấp"
              value={formatMoney(d.unpaidTotal)}
              tone="purple"
              sub={`Đã thanh toán ${formatMoney(d.paidTotal)}`}
              percent={debt > 0 ? (d.paidTotal / debt) * 100 : 0}
              onClick={() => navigate('/thanh-toan?tab=valuation')}
            />
          </KpiStrip>
          <div className="home-grid home-grid--side">
            <Panel title="Chờ tôi xử lý theo loại" extra={<SeeAll to="/trinh-ky" />} flush>
              {d.pendingByType.length === 0 ? (
                <Empty
                  className="home-empty"
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description="Không có tài liệu chờ xử lý"
                />
              ) : (
                <ul className="home-queue">
                  {d.pendingByType.map((p) => (
                    <li key={p.type}>
                      <span className="home-queue__icon" aria-hidden>
                        <FileClock size={18} strokeWidth={1.75} />
                      </span>
                      <span className="home-queue__text">
                        <StatusTag domain="documentType" value={p.type} />
                        <span className="home-queue__count">
                          {p.count} tài liệu
                          {p.overdue > 0 && (
                            <span className="home-queue__overdue">
                              <CircleAlert size={13} aria-hidden /> {p.overdue} quá hạn
                            </span>
                          )}
                        </span>
                      </span>
                      <Button size="small" onClick={() => navigate(`/trinh-ky?documentType=${p.type}`)}>
                        Xử lý
                      </Button>
                    </li>
                  ))}
                </ul>
              )}
            </Panel>
            <PaymentPanel data={d.payments} title="Giá trị thanh toán 12 tháng gần nhất" />
          </div>
          <div className="home-grid home-grid--halves">
            <Panel
              title={`Hợp đồng sắp hết hạn (≤ ${d.expiryWarningDays} ngày)`}
              extra={<SeeAll to="/hop-dong" />}
              flush
            >
              <Table
                rowKey="id"
                size="small"
                pagination={false}
                scroll={d.expiringContracts.length ? { x: 520 } : undefined}
                dataSource={d.expiringContracts}
                locale={{ emptyText: 'Không có hợp đồng sắp hết hạn' }}
                columns={[
                  {
                    title: 'Hợp đồng',
                    dataIndex: 'number',
                    render: (v: string, r) => (
                      <Link to={`/hop-dong/${r.id}`} className="mono">
                        {v}
                      </Link>
                    ),
                  },
                  { title: 'Nhà cung cấp', dataIndex: 'supplierName', ellipsis: true },
                  {
                    title: 'Hết hạn',
                    dataIndex: 'expiryDate',
                    width: 104,
                    render: (v: string) => formatDate(v),
                  },
                  {
                    title: 'Còn',
                    dataIndex: 'daysLeft',
                    align: 'right',
                    width: 100,
                    render: (v: number) => (
                      <Typography.Text type={v <= 7 ? 'danger' : 'warning'} strong>
                        {v < 0 ? 'Đã hết hạn' : `${v} ngày`}
                      </Typography.Text>
                    ),
                  },
                ]}
              />
            </Panel>
            <Panel title={`Mặt hàng sắp hết số lượng hợp đồng (≤ ${d.lowQuantityPercent}%)`} flush>
              <Table
                rowKey={(r) => `${r.contractId}-${r.itemName}`}
                size="small"
                pagination={false}
                scroll={d.lowQuantityItems.length ? { x: 520 } : undefined}
                dataSource={d.lowQuantityItems}
                locale={{ emptyText: 'Không có mặt hàng sắp hết số lượng' }}
                columns={[
                  {
                    title: 'Hợp đồng',
                    dataIndex: 'contractNumber',
                    render: (v: string, r) => (
                      <Link to={`/hop-dong/${r.contractId}`} className="mono">
                        {v}
                      </Link>
                    ),
                  },
                  { title: 'Mặt hàng', dataIndex: 'itemName', ellipsis: true },
                  {
                    title: 'Còn lại',
                    key: 'remaining',
                    align: 'right',
                    width: 200,
                    render: (_, r) => (
                      <span className="home-remaining">
                        <span>
                          {formatQuantity(r.remaining)} / {formatQuantity(r.quantity)} {r.unit} ({r.percent}%)
                        </span>
                        <span className="home-remaining__bar" aria-hidden>
                          <span
                            style={{
                              width: `${Math.max(0, Math.min(100, r.percent))}%`,
                              background: `var(--bp-${r.percent <= 10 ? 'danger' : 'warning'}-solid)`,
                            }}
                          />
                        </span>
                      </span>
                    ),
                  },
                ]}
              />
            </Panel>
          </div>
          {can(P.SettingManage, P.AuditView) && <SystemStatus />}
        </div>
      )}
    </>
  );
}

/** Tổng quan Cổng NCC (spec §9.2-1). */
export function SupplierHomePage() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const dashboard = useSupplierDashboard();
  const d = dashboard.data;
  return (
    <>
      <PageHeader title={`Xin chào, ${user?.fullName ?? ''}`} subtitle={user?.supplierName ?? undefined} />
      {!d ? (
        <Card loading={dashboard.isLoading} />
      ) : (
        <div className="home">
          <KpiStrip>
            <KpiCard
              icon={<FileSignature {...ICON} />}
              label="Tài liệu chờ ký"
              value={d.pendingSigning}
              sub="Hợp đồng, phụ lục, biên bản cần Quý công ty ký"
              onClick={() => navigate('/ncc/trinh-ky')}
            />
            <KpiCard
              icon={<Truck {...ICON} />}
              label="Đơn gọi hàng chờ xác nhận"
              value={d.callOrdersAwaiting}
              tone={d.callOrdersOverdue > 0 ? 'orange' : 'teal'}
              sub={
                d.callOrdersOverdue > 0
                  ? `${d.callOrdersOverdue} đơn quá hạn xác nhận`
                  : 'Không có đơn quá hạn'
              }
              onClick={() => navigate('/ncc/goi-hang')}
            />
            <KpiCard
              icon={<Building2 {...ICON} />}
              label="Hợp đồng đang hiệu lực"
              value={d.effectiveContracts}
              tone="success"
              onClick={() => navigate('/ncc/hop-dong')}
            />
            <KpiCard
              icon={<Receipt {...ICON} />}
              label="Phiếu nhập kho cần bổ sung hóa đơn"
              value={d.receiptsWithoutInvoice}
              tone={d.receiptsWithoutInvoice > 0 ? 'danger' : 'success'}
              onClick={() => navigate('/ncc/nhap-kho?hasInvoice=false')}
            />
          </KpiStrip>
          <div className="home-grid home-grid--side">
            <Panel title="Công nợ bệnh viện còn phải trả">
              <ul className="home-debt">
                <li>
                  <span className="home-debt__label">Đã xác định giá trị (08a), chưa thanh toán</span>
                  <span className="home-debt__value" style={{ color: 'var(--bp-orange-fg)' }}>
                    {formatMoney(d.valuedUnpaid)}
                  </span>
                </li>
                <li>
                  <span className="home-debt__label">Đã nghiệm thu, chưa lập 08a</span>
                  <span className="home-debt__value">{formatMoney(d.acceptedNotValued)}</span>
                </li>
                <li>
                  <span className="home-debt__label">
                    <PackageCheck size={14} aria-hidden /> Đã được thanh toán
                  </span>
                  <span className="home-debt__value" style={{ color: 'var(--bp-success-fg)' }}>
                    {formatMoney(d.paidTotal)}
                  </span>
                </li>
              </ul>
            </Panel>
            <PaymentPanel data={d.payments} title="Thanh toán 12 tháng gần nhất" />
          </div>
        </div>
      )}
    </>
  );
}
