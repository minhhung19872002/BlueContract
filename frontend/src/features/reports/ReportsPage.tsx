import { InputNumber, Select, Table, Typography } from 'antd';
import { Link, useSearchParams } from 'react-router-dom';
import { useState } from 'react';
import { DOMAIN_OPTIONS, type ProcurementDomain } from '../../api/proposals';
import {
  REPORTS,
  useContractExecution,
  usePaymentReport,
  useSigningReport,
  useSupplierDebt,
  type ContractExecutionRowDto,
  type PaymentDimension,
  type PaymentPeriod,
  type PaymentReportRowDto,
  type SupplierDebtRowDto,
} from '../../api/reports';
import { BarChart } from '../../components/BarChart';
import { HBarChart } from '../../components/HBarChart';
import { StatusTag, statusOptions, type Tone } from '../../components/StatusTag';
import { ExportExcelButton, PageHeader } from '../../components/common';
import { SupplierSelect } from '../../components/inputs';
import { Panel } from '../../components/layout';
import { KpiCard, KpiStrip } from '../../components/patterns';
import { shortMoney } from '../../utils/chart';
import { formatDate, formatDateTime, formatMoney } from '../../utils/format';
import './reports.css';

const money = (v: number) => formatMoney(v);
const percent = (part: number, whole: number) => (whole > 0 ? Math.round((part / whole) * 1000) / 10 : 0);
/** "82,4 tỷ ₫" for the KPI cards; the exact amount is in the table. */
const big = (v: number) => (v === 0 ? '0 ₫' : `${shortMoney(v)} ₫`);
/** Hours → "1,8 ngày", "5,5 giờ" or "12 phút" (the demo signs within minutes). */
const days = (hours: number) =>
  hours >= 24
    ? `${(hours / 24).toLocaleString('vi-VN', { maximumFractionDigits: 1 })} ngày`
    : hours >= 1
      ? `${hours.toLocaleString('vi-VN', { maximumFractionDigits: 1 })} giờ`
      : `${Math.round(hours * 60)} phút`;

const TABS = [
  { key: 'execution', label: 'Tình hình thực hiện hợp đồng' },
  { key: 'debt', label: 'Công nợ theo nhà cung cấp' },
  { key: 'payments', label: 'Giá trị thanh toán' },
  { key: 'signing', label: 'Tiến độ ký' },
] as const;

/** Báo cáo (spec §9.1-13, design/Bao cao.dc.html): thực hiện HĐ, công nợ NCC, thanh toán, tiến độ ký — tất cả xuất Excel. */
export function ReportsPage() {
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'execution';
  return (
    <>
      <PageHeader
        title="Báo cáo"
        subtitle="Tình hình thực hiện hợp đồng, công nợ, thanh toán và tiến độ ký"
      />
      <div role="tablist" className="data-table__tabs">
        {TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            role="tab"
            aria-selected={t.key === tab}
            className={`data-table__tab${t.key === tab ? ' data-table__tab--active' : ''}`}
            onClick={() => setParams({ tab: t.key })}
          >
            {t.label}
          </button>
        ))}
      </div>
      <div className="report" role="tabpanel">
        {tab === 'debt' ? (
          <DebtReport />
        ) : tab === 'payments' ? (
          <PaymentReport />
        ) : tab === 'signing' ? (
          <SigningReport />
        ) : (
          <ExecutionReport />
        )}
      </div>
    </>
  );
}

function ExecutionReport() {
  const [filters, setFilters] = useState<{
    supplierId?: string;
    status?: string;
    domain?: ProcurementDomain;
  }>({});
  const report = useContractExecution(filters);
  const rows = report.data ?? [];
  const sum = (k: keyof ContractExecutionRowDto) => rows.reduce((s, r) => s + Number(r[k]), 0);
  const value = sum('contractValue');
  return (
    <>
      <div className="report__filters">
        <SupplierSelect
          allowClear
          style={{ width: 260 }}
          value={filters.supplierId}
          onChange={(v) => setFilters({ ...filters, supplierId: v })}
        />
        <Select
          allowClear
          placeholder="Mảng"
          style={{ width: 200 }}
          options={DOMAIN_OPTIONS}
          value={filters.domain}
          onChange={(v) => setFilters({ ...filters, domain: v })}
        />
        <Select
          allowClear
          placeholder="Trạng thái"
          style={{ width: 180 }}
          options={statusOptions('contractStatus')}
          value={filters.status}
          onChange={(v) => setFilters({ ...filters, status: v })}
        />
        <span className="report__spacer" />
        <ExportExcelButton
          url={`${REPORTS}/contract-execution/export`}
          params={filters}
          fileName="thuc-hien-hop-dong.xlsx"
        />
      </div>
      <KpiStrip>
        <KpiCard label="Giá trị hợp đồng" value={big(value)} sub={`${rows.length} hợp đồng`} />
        <KpiCard
          label="Đã nhập kho"
          value={big(sum('receivedValue'))}
          tone="teal"
          percent={percent(sum('receivedValue'), value)}
          sub={`${percent(sum('receivedValue'), value)}% giá trị hợp đồng`}
        />
        <KpiCard
          label="Đã thanh toán"
          value={big(sum('paidValue'))}
          tone="success"
          percent={percent(sum('paidValue'), value)}
          sub={`${percent(sum('paidValue'), value)}% giá trị hợp đồng`}
        />
        <KpiCard
          label="Còn lại"
          value={big(sum('remainingValue'))}
          tone="orange"
          sub={`${percent(sum('remainingValue'), value)}% giá trị hợp đồng`}
        />
      </KpiStrip>
      <Panel title="Tình hình thực hiện hợp đồng" flush>
        <Table<ContractExecutionRowDto>
          rowKey="contractId"
          size="small"
          loading={report.isFetching}
          dataSource={rows}
          scroll={{ x: 'max-content' }}
          pagination={{ pageSize: 50, hideOnSinglePage: true }}
          summary={() => (
            <Table.Summary.Row>
              <Table.Summary.Cell index={0} colSpan={4}>
                <b>Tổng cộng ({rows.length} hợp đồng)</b>
              </Table.Summary.Cell>
              {(
                [
                  'contractValue',
                  'calledValue',
                  'receivedValue',
                  'acceptedValue',
                  'paidValue',
                  'remainingValue',
                ] as const
              ).map((k, i) => (
                <Table.Summary.Cell key={k} index={i + 1} align="right">
                  <b>{money(sum(k))}</b>
                </Table.Summary.Cell>
              ))}
              <Table.Summary.Cell index={8} />
            </Table.Summary.Row>
          )}
          columns={[
            {
              title: 'Hợp đồng',
              dataIndex: 'contractNumber',
              fixed: 'left',
              render: (v: string, r) => (
                <Link to={`/hop-dong/${r.contractId}`} className="mono">
                  {v}
                </Link>
              ),
            },
            { title: 'Nhà cung cấp', dataIndex: 'supplierName', width: 220 },
            {
              title: 'Trạng thái',
              dataIndex: 'status',
              render: (v: string) => <StatusTag domain="contractStatus" value={v} />,
            },
            { title: 'Hết hạn', dataIndex: 'expiryDate', render: (v?: string | null) => formatDate(v) },
            { title: 'Giá trị HĐ', dataIndex: 'contractValue', align: 'right', render: money },
            { title: 'Đã gọi', dataIndex: 'calledValue', align: 'right', render: money },
            { title: 'Đã nhập kho', dataIndex: 'receivedValue', align: 'right', render: money },
            { title: 'Đã nghiệm thu', dataIndex: 'acceptedValue', align: 'right', render: money },
            { title: 'Đã thanh toán', dataIndex: 'paidValue', align: 'right', render: money },
            { title: 'Còn lại', dataIndex: 'remainingValue', align: 'right', render: money },
            {
              title: '% thực hiện',
              dataIndex: 'executionPercent',
              width: 150,
              render: (v: number) => (
                <span className="report-progress">
                  <span className="report-progress__bar" aria-hidden>
                    <span
                      style={{
                        width: `${Math.min(100, Math.max(0, v))}%`,
                        background: `var(--bp-${v >= 100 ? 'success' : 'primary'}-solid)`,
                      }}
                    />
                  </span>
                  {v}%
                </span>
              ),
            },
          ]}
        />
      </Panel>
    </>
  );
}

function DebtReport() {
  const report = useSupplierDebt();
  const rows = report.data ?? [];
  const total = (k: keyof SupplierDebtRowDto) => rows.reduce((s, r) => s + Number(r[k]), 0);
  const owing = rows.filter((r) => r.debtValue > 0);
  const top = [...owing].sort((a, b) => b.debtValue - a.debtValue).slice(0, 5);
  return (
    <>
      <div className="report__filters">
        <Typography.Text type="secondary">
          Còn phải trả = đã nghiệm thu − đã thanh toán (ủy nhiệm chi + chốt công nợ) − thu hồi tạm ứng
        </Typography.Text>
        <span className="report__spacer" />
        <ExportExcelButton url={`${REPORTS}/supplier-debt/export`} fileName="cong-no-nha-cung-cap.xlsx" />
      </div>
      <KpiStrip>
        <KpiCard
          label="Tổng công nợ"
          value={big(total('debtValue'))}
          tone={total('debtValue') > 0 ? 'orange' : 'success'}
          sub={`${owing.length} nhà cung cấp còn phải trả`}
        />
        <KpiCard
          label="Đã nghiệm thu"
          value={big(total('acceptedValue'))}
          sub={`${rows.length} nhà cung cấp`}
        />
        <KpiCard label="Đã lập 08a" value={big(total('valuedValue'))} tone="purple" />
        <KpiCard label="Đã thanh toán" value={big(total('paidValue'))} tone="success" />
      </KpiStrip>
      <Panel title="Top nhà cung cấp theo công nợ">
        <HBarChart
          label="Top nhà cung cấp theo công nợ"
          empty="Không có nhà cung cấp còn công nợ"
          bars={top.map((r) => ({
            key: r.supplierId,
            label: r.supplierName,
            value: r.debtValue,
            display: big(r.debtValue),
          }))}
        />
      </Panel>
      <Panel title="Công nợ theo nhà cung cấp" flush>
        <Table<SupplierDebtRowDto>
          rowKey="supplierId"
          size="small"
          loading={report.isFetching}
          dataSource={rows}
          pagination={false}
          scroll={{ x: 'max-content' }}
          summary={() => (
            <Table.Summary.Row>
              <Table.Summary.Cell index={0} colSpan={3}>
                <b>Tổng cộng</b>
              </Table.Summary.Cell>
              {(['contractValue', 'acceptedValue', 'valuedValue', 'paidValue', 'debtValue'] as const).map(
                (k, i) => (
                  <Table.Summary.Cell key={k} index={i + 1} align="right">
                    <b>{money(total(k))}</b>
                  </Table.Summary.Cell>
                ),
              )}
            </Table.Summary.Row>
          )}
          columns={[
            { title: 'MST', dataIndex: 'taxCode', render: (v: string) => <span className="mono">{v}</span> },
            { title: 'Nhà cung cấp', dataIndex: 'supplierName' },
            { title: 'Số HĐ', dataIndex: 'contractCount', align: 'right' },
            { title: 'Giá trị HĐ', dataIndex: 'contractValue', align: 'right', render: money },
            { title: 'Đã nghiệm thu', dataIndex: 'acceptedValue', align: 'right', render: money },
            { title: 'Đã lập 08a', dataIndex: 'valuedValue', align: 'right', render: money },
            { title: 'Đã thanh toán', dataIndex: 'paidValue', align: 'right', render: money },
            {
              title: 'Còn phải trả',
              dataIndex: 'debtValue',
              align: 'right',
              render: (v: number) => (
                <Typography.Text type={v > 0 ? 'danger' : undefined} strong>
                  {money(v)}
                </Typography.Text>
              ),
            },
          ]}
        />
      </Panel>
    </>
  );
}

function PaymentReport() {
  const [filters, setFilters] = useState<{
    year: number;
    period: PaymentPeriod;
    dimension: PaymentDimension;
  }>({
    year: new Date().getFullYear(),
    period: 'Month',
    dimension: 'None',
  });
  const report = usePaymentReport(filters);
  const rows = report.data ?? [];
  const byPeriod = Object.values(
    rows.reduce<Record<string, { period: string; paid: number }>>((acc, r) => {
      acc[r.period] = { period: r.period, paid: (acc[r.period]?.paid ?? 0) + r.paidAmount };
      return acc;
    }, {}),
  );
  const paid = byPeriod.reduce((s, p) => s + p.paid, 0);
  const peak = byPeriod.reduce<{ period: string; paid: number } | null>(
    (best, p) => (!best || p.paid > best.paid ? p : best),
    null,
  );
  const unit = filters.period === 'Month' ? 'tháng' : filters.period === 'Quarter' ? 'quý' : 'năm';
  return (
    <>
      <div className="report__filters">
        <span className="report__label">Năm</span>
        <InputNumber
          min={2020}
          max={2100}
          value={filters.year}
          onChange={(v) => v && setFilters({ ...filters, year: v })}
        />
        <Select
          style={{ width: 160 }}
          value={filters.period}
          onChange={(v) => setFilters({ ...filters, period: v })}
          options={[
            { value: 'Month', label: 'Theo tháng' },
            { value: 'Quarter', label: 'Theo quý' },
            { value: 'Year', label: 'Theo năm (5 năm)' },
          ]}
        />
        <Select
          style={{ width: 190 }}
          value={filters.dimension}
          onChange={(v) => setFilters({ ...filters, dimension: v })}
          options={[
            { value: 'None', label: 'Không chia nhóm' },
            { value: 'Domain', label: 'Theo mảng' },
            { value: 'Facility', label: 'Theo cơ sở' },
            { value: 'Supplier', label: 'Theo nhà cung cấp' },
          ]}
        />
        <span className="report__spacer" />
        <ExportExcelButton
          url={`${REPORTS}/payments/export`}
          params={filters}
          fileName="gia-tri-thanh-toan.xlsx"
        />
      </div>
      <KpiStrip>
        <KpiCard label="Đã thanh toán" value={big(paid)} tone="success" sub={`Năm ${filters.year}`} />
        <KpiCard
          label={`Trung bình / ${unit}`}
          value={big(byPeriod.length ? paid / byPeriod.length : 0)}
          sub={`${byPeriod.length} ${unit} có thanh toán`}
        />
        <KpiCard
          label={`${unit.charAt(0).toUpperCase()}${unit.slice(1)} cao nhất`}
          value={peak ? big(peak.paid) : '—'}
          sub={peak?.period}
        />
        <KpiCard
          label="Số ủy nhiệm chi"
          value={rows.reduce((s, r) => s + r.paymentCount, 0).toLocaleString('vi-VN')}
        />
      </KpiStrip>
      <Panel title={`Giá trị thanh toán theo ${unit}`}>
        <BarChart
          data={byPeriod}
          category="period"
          label={`Giá trị thanh toán theo ${unit}`}
          series={[{ key: 'paid', label: 'Đã thanh toán (UNC)', color: 'var(--bp-primary-solid)' }]}
        />
      </Panel>
      <Panel title={`Thanh toán theo ${unit}`} flush>
        <Table<PaymentReportRowDto>
          rowKey={(r) => `${r.period}-${r.group}`}
          size="small"
          loading={report.isFetching}
          dataSource={rows}
          pagination={{ pageSize: 20, hideOnSinglePage: true }}
          summary={() => (
            <Table.Summary.Row>
              <Table.Summary.Cell index={0} colSpan={2}>
                <b>Tổng cộng</b>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={2} align="right">
                <b>{rows.reduce((s, r) => s + r.paymentCount, 0)}</b>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={3} align="right">
                <b>{money(paid)}</b>
              </Table.Summary.Cell>
            </Table.Summary.Row>
          )}
          columns={[
            { title: 'Kỳ', dataIndex: 'period' },
            { title: 'Nhóm', dataIndex: 'group' },
            { title: 'Số UNC', dataIndex: 'paymentCount', align: 'right' },
            { title: 'Đã thanh toán', dataIndex: 'paidAmount', align: 'right', render: money },
          ]}
        />
      </Panel>
    </>
  );
}

function SigningReport() {
  const report = useSigningReport();
  const steps = report.data?.steps ?? [];
  const backlog = report.data?.backlog ?? [];
  const completed = steps.reduce((s, x) => s + x.completed, 0);
  const avgHours =
    completed > 0 ? steps.reduce((s, x) => s + x.averageHours * x.completed, 0) / completed : 0;
  const slowest = steps.reduce<(typeof steps)[number] | null>(
    (best, x) => (!best || x.averageHours > best.averageHours ? x : best),
    null,
  );
  const ranked = [...steps].sort((a, b) => b.averageHours - a.averageHours).slice(0, 8);
  const tone = (hours: number, overdue: number): Tone =>
    overdue > 0 ? 'danger' : slowest && hours >= slowest.averageHours * 0.75 ? 'warning' : 'primary';
  return (
    <>
      <div className="report__filters">
        <Typography.Text type="secondary">
          Số liệu 6 tháng gần nhất, tính theo các bước đã hoàn tất
        </Typography.Text>
        <span className="report__spacer" />
        <ExportExcelButton url={`${REPORTS}/signing/export`} fileName="tien-do-ky.xlsx" />
      </div>
      <KpiStrip>
        <KpiCard
          label="Thời gian xử lý trung bình"
          value={days(avgHours)}
          sub={`mỗi bước · ${completed} lượt`}
        />
        <KpiCard
          label="Tài liệu đang chờ"
          value={backlog.reduce((s, x) => s + x.pending, 0).toLocaleString('vi-VN')}
          sub={`${backlog.length} người / vai trò được giao`}
        />
        <KpiCard
          label="Quá hạn"
          value={backlog.reduce((s, x) => s + x.overdue, 0).toLocaleString('vi-VN')}
          tone={backlog.some((x) => x.overdue > 0) ? 'danger' : 'success'}
          sub="đang mở"
        />
        <KpiCard
          label="Bước chậm nhất"
          value={slowest?.stepName ?? '—'}
          tone="warning"
          sub={slowest ? days(slowest.averageHours) : undefined}
        />
      </KpiStrip>
      <Panel title="Thời gian xử lý trung bình theo bước">
        <HBarChart
          label="Thời gian xử lý trung bình theo bước"
          bars={ranked.map((x) => ({
            key: `${x.workflowCode}-${x.stepCode}`,
            label: `${x.stepName} · ${x.workflowCode}`,
            value: x.averageHours,
            display: days(x.averageHours),
            tone: tone(x.averageHours, x.overdue),
          }))}
        />
      </Panel>
      <div className="report__grid">
        <Panel title="Chi tiết theo bước" flush>
          <Table
            rowKey={(x) => `${x.workflowCode}-${x.stepCode}`}
            size="small"
            loading={report.isFetching}
            dataSource={steps}
            scroll={{ x: 'max-content' }}
            pagination={{ pageSize: 20, hideOnSinglePage: true }}
            columns={[
              {
                title: 'Luồng',
                dataIndex: 'workflowCode',
                render: (v: string) => <span className="mono">{v}</span>,
              },
              { title: 'Bước', dataIndex: 'stepName' },
              { title: 'Số lần', dataIndex: 'completed', align: 'right' },
              { title: 'TB (giờ)', dataIndex: 'averageHours', align: 'right' },
              { title: 'Lâu nhất (giờ)', dataIndex: 'maxHours', align: 'right' },
              {
                title: 'Quá SLA',
                dataIndex: 'overdue',
                align: 'right',
                render: (v: number) => (
                  <Typography.Text type={v > 0 ? 'danger' : undefined}>{v}</Typography.Text>
                ),
              },
            ]}
          />
        </Panel>
        <Panel title="Tài liệu tồn đọng theo người / vai trò" flush>
          <Table
            rowKey={(x) => `${x.assigneeType}-${x.assignee}`}
            size="small"
            loading={report.isFetching}
            dataSource={backlog}
            scroll={{ x: 'max-content' }}
            pagination={false}
            columns={[
              {
                title: 'Được giao',
                dataIndex: 'assignee',
                render: (v: string, x) => `${v} (${x.assigneeType})`,
              },
              { title: 'Chờ', dataIndex: 'pending', align: 'right' },
              {
                title: 'Quá hạn',
                dataIndex: 'overdue',
                align: 'right',
                render: (v: number) => (
                  <Typography.Text type={v > 0 ? 'danger' : undefined}>{v}</Typography.Text>
                ),
              },
              {
                title: 'Cũ nhất từ',
                dataIndex: 'oldestSince',
                render: (v?: string | null) => formatDateTime(v),
              },
            ]}
          />
        </Panel>
      </div>
    </>
  );
}
