import { Card, Col, InputNumber, Row, Select, Space, Table, Tabs, Typography } from 'antd';
import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
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
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { ExportExcelButton, PageHeader } from '../../components/common';
import { SupplierSelect } from '../../components/inputs';
import { formatDate, formatDateTime, formatMoney } from '../../utils/format';

const money = (v: number) => formatMoney(v);

/** Báo cáo (spec §9.1-13): thực hiện HĐ, công nợ NCC, thanh toán theo kỳ / mảng / cơ sở, tiến độ ký — tất cả xuất Excel. */
export function ReportsPage() {
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'execution';
  return (
    <>
      <PageHeader
        title="Báo cáo"
        subtitle="Số liệu tính trực tiếp từ hợp đồng, phiếu nhập kho, biên bản và ủy nhiệm chi"
      />
      <Tabs
        activeKey={tab}
        onChange={(key) => setParams({ tab: key })}
        items={[
          { key: 'execution', label: 'Tình hình thực hiện hợp đồng', children: <ExecutionReport /> },
          { key: 'debt', label: 'Công nợ theo nhà cung cấp', children: <DebtReport /> },
          { key: 'payments', label: 'Giá trị thanh toán', children: <PaymentReport /> },
          { key: 'signing', label: 'Tiến độ ký', children: <SigningReport /> },
        ]}
      />
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
  return (
    <Card>
      <Space wrap style={{ marginBottom: 12 }}>
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
        <ExportExcelButton
          url={`${REPORTS}/contract-execution/export`}
          params={filters}
          fileName="thuc-hien-hop-dong.xlsx"
        />
      </Space>
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
            render: (v: string, r) => <Link to={`/hop-dong/${r.contractId}`}>{v}</Link>,
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
            align: 'right',
            render: (v: number) => `${v}%`,
          },
        ]}
      />
    </Card>
  );
}

function DebtReport() {
  const report = useSupplierDebt();
  const rows = report.data ?? [];
  return (
    <Card>
      <Space style={{ marginBottom: 12 }}>
        <Typography.Text type="secondary">
          Còn phải trả = đã nghiệm thu − đã thanh toán (ủy nhiệm chi + chốt công nợ) − thu hồi tạm ứng
        </Typography.Text>
        <ExportExcelButton url={`${REPORTS}/supplier-debt/export`} fileName="cong-no-nha-cung-cap.xlsx" />
      </Space>
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
                  <b>{money(rows.reduce((s, r) => s + r[k], 0))}</b>
                </Table.Summary.Cell>
              ),
            )}
          </Table.Summary.Row>
        )}
        columns={[
          { title: 'Nhà cung cấp', dataIndex: 'supplierName' },
          { title: 'MST', dataIndex: 'taxCode' },
          { title: 'Số HĐ', dataIndex: 'contractCount', align: 'center' },
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
    </Card>
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
  return (
    <Card>
      <Space wrap style={{ marginBottom: 12 }}>
        <Typography.Text>Năm</Typography.Text>
        <InputNumber
          min={2020}
          max={2100}
          value={filters.year}
          onChange={(v) => v && setFilters({ ...filters, year: v })}
        />
        <Select
          style={{ width: 140 }}
          value={filters.period}
          onChange={(v) => setFilters({ ...filters, period: v })}
          options={[
            { value: 'Month', label: 'Theo tháng' },
            { value: 'Quarter', label: 'Theo quý' },
            { value: 'Year', label: 'Theo năm (5 năm)' },
          ]}
        />
        <Select
          style={{ width: 180 }}
          value={filters.dimension}
          onChange={(v) => setFilters({ ...filters, dimension: v })}
          options={[
            { value: 'None', label: 'Không chia nhóm' },
            { value: 'Domain', label: 'Theo mảng' },
            { value: 'Facility', label: 'Theo cơ sở' },
            { value: 'Supplier', label: 'Theo nhà cung cấp' },
          ]}
        />
        <ExportExcelButton
          url={`${REPORTS}/payments/export`}
          params={filters}
          fileName="gia-tri-thanh-toan.xlsx"
        />
      </Space>
      <Row gutter={[16, 16]}>
        <Col xs={24} xl={12}>
          <BarChart
            data={byPeriod}
            category="period"
            series={[{ key: 'paid', label: 'Đã thanh toán (UNC)', color: '#1677ff' }]}
          />
        </Col>
        <Col xs={24} xl={12}>
          <Table<PaymentReportRowDto>
            rowKey={(r) => `${r.period}-${r.group}`}
            size="small"
            loading={report.isFetching}
            dataSource={rows}
            pagination={{ pageSize: 20, hideOnSinglePage: true }}
            columns={[
              { title: 'Kỳ', dataIndex: 'period' },
              { title: 'Nhóm', dataIndex: 'group' },
              { title: 'Số UNC', dataIndex: 'paymentCount', align: 'center' },
              { title: 'Đã thanh toán', dataIndex: 'paidAmount', align: 'right', render: money },
            ]}
          />
        </Col>
      </Row>
    </Card>
  );
}

function SigningReport() {
  const report = useSigningReport();
  const r = report.data;
  return (
    <Row gutter={[16, 16]}>
      <Col xs={24} xl={14}>
        <Card
          title="Thời gian xử lý trung bình mỗi bước (6 tháng)"
          extra={<ExportExcelButton url={`${REPORTS}/signing/export`} fileName="tien-do-ky.xlsx" />}
        >
          <Table
            rowKey={(x) => `${x.workflowCode}-${x.stepCode}`}
            size="small"
            loading={report.isFetching}
            dataSource={r?.steps ?? []}
            pagination={{ pageSize: 20, hideOnSinglePage: true }}
            columns={[
              { title: 'Luồng', dataIndex: 'workflowCode' },
              { title: 'Bước', dataIndex: 'stepName' },
              { title: 'Số lần', dataIndex: 'completed', align: 'center' },
              { title: 'TB (giờ)', dataIndex: 'averageHours', align: 'right' },
              { title: 'Lâu nhất (giờ)', dataIndex: 'maxHours', align: 'right' },
              { title: 'Quá SLA', dataIndex: 'overdue', align: 'center' },
            ]}
          />
        </Card>
      </Col>
      <Col xs={24} xl={10}>
        <Card title="Tài liệu tồn đọng theo người / vai trò được giao">
          <Table
            rowKey={(x) => `${x.assigneeType}-${x.assignee}`}
            size="small"
            loading={report.isFetching}
            dataSource={r?.backlog ?? []}
            pagination={false}
            columns={[
              {
                title: 'Được giao',
                dataIndex: 'assignee',
                render: (v: string, x) => `${v} (${x.assigneeType})`,
              },
              { title: 'Chờ', dataIndex: 'pending', align: 'center' },
              {
                title: 'Quá hạn',
                dataIndex: 'overdue',
                align: 'center',
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
        </Card>
      </Col>
    </Row>
  );
}
