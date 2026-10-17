import {
  Alert,
  App,
  Button,
  Card,
  Checkbox,
  Col,
  DatePicker,
  Empty,
  Form,
  Input,
  Row,
  Select,
  Space,
  Statistic,
  Table,
  Typography,
} from 'antd';
import { SaveOutlined, SearchOutlined } from '@ant-design/icons';
import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import dayjs from '../../i18n/dayjs';
import { useLookup } from '../../api/admin';
import {
  DOMAIN_OPTIONS,
  proposalApi,
  useProposal,
  useProposalAction,
  useProposalCandidates,
  type ProcurementDomain,
  type ProposalCandidateDto,
  type ProposalDto,
} from '../../api/proposals';
import { useAuth } from '../../auth/useAuth';
import { useApiError } from '../../components/common';
import { useFormDraft } from '../../hooks/useFormDraft';
import { DocHeader, StickyActionBar } from '../../components/layout';
import { QuantityInput } from '../../components/inputs';
import { formatMoney, formatQuantity } from '../../utils/format';
import { candidateLabel, filterCandidates } from './candidateFilter';

interface Header {
  departmentId?: string;
  warehouseId?: string;
  domain?: ProcurementDomain;
  period?: [dayjs.Dayjs, dayjs.Dayjs];
  note?: string;
}

type Entry = { qty?: number; note?: string };

/** Lập / sửa phiếu đề xuất (spec §9.1-7): lưới mặt hàng có cột X–N–T từ HIS, SL còn lại HĐ, ô nhập SL đề xuất. */
export function ProposalEditorPage() {
  const { id } = useParams<{ id: string }>();
  const existing = useProposal(id);
  const { user } = useAuth();
  if (id && !existing.data) return <Card loading />;
  const p = existing.data;
  return (
    <ProposalEditor
      key={id ?? 'new'}
      proposal={p}
      initialHeader={
        p
          ? {
              departmentId: p.departmentId,
              warehouseId: p.warehouseId,
              domain: p.domain,
              period: [dayjs(p.periodFrom), dayjs(p.periodTo)],
              note: p.note ?? undefined,
            }
          : {
              departmentId: user?.departmentId ?? undefined,
              domain: undefined,
              period: [dayjs().startOf('month'), dayjs().endOf('month')],
            }
      }
      initialEntries={Object.fromEntries(
        (p?.lines ?? []).map((l) => [
          l.contractItemId,
          { qty: l.proposedQuantity, note: l.note ?? undefined },
        ]),
      )}
    />
  );
}

function ProposalEditor({
  proposal,
  initialHeader,
  initialEntries,
}: {
  proposal?: ProposalDto;
  initialHeader: Header;
  initialEntries: Record<string, Entry>;
}) {
  const editing = !!proposal;
  const [form] = Form.useForm<Header>();
  const [entries, setEntries] = useState<Record<string, Entry>>(initialEntries);
  const [search, setSearch] = useState('');
  const [onlyChosen, setOnlyChosen] = useState(false);
  const navigate = useNavigate();
  const { message } = App.useApp();
  const onError = useApiError();
  const departments = useLookup('departments');
  const warehouses = useLookup('warehouses');
  const departmentId = Form.useWatch('departmentId', form) ?? initialHeader.departmentId;
  const warehouseId = Form.useWatch('warehouseId', form) ?? initialHeader.warehouseId;
  const domain = Form.useWatch('domain', form) ?? initialHeader.domain;
  const period = Form.useWatch('period', form) ?? initialHeader.period;
  const candidates = useProposalCandidates({
    domain,
    warehouseId,
    departmentId,
    from: period?.[0]?.format('YYYY-MM-DD'),
    to: period?.[1]?.format('YYYY-MM-DD'),
  });

  // Lưu nháp tự động (spec §9.3) for a new proposal.
  const draft = useFormDraft({
    key: editing ? null : 'proposal-new',
    form,
    extra: entries,
    onRestoreExtra: setEntries,
  });

  const save = useProposalAction(async () => {
    const h = await form.validateFields();
    const body = {
      departmentId: h.departmentId!,
      warehouseId: h.warehouseId!,
      domain: h.domain!,
      periodFrom: h.period![0].format('YYYY-MM-DD'),
      periodTo: h.period![1].format('YYYY-MM-DD'),
      note: h.note ?? null,
      lines: Object.entries(entries)
        .filter(([, e]) => (e.qty ?? 0) > 0)
        .map(([contractItemId, e]) => ({ contractItemId, proposedQuantity: e.qty!, note: e.note ?? null })),
    };
    if (body.lines.length === 0) throw new Error('Nhập số lượng đề xuất cho ít nhất một mặt hàng.');
    if (editing) {
      await proposalApi.update(proposal.id, body);
      return proposal.id;
    }
    return proposalApi.create(body);
  });

  const rows = candidates.data?.items ?? [];
  const over = rows.filter((r) => (entries[r.contractItemId]?.qty ?? 0) > r.contractRemaining);
  const chosen = rows.filter((r) => (entries[r.contractItemId]?.qty ?? 0) > 0);
  const visible = filterCandidates(rows, search, onlyChosen, entries);
  const total = useMemo(
    () => chosen.reduce((s, r) => s + Math.round((entries[r.contractItemId]?.qty ?? 0) * r.unitPrice), 0),
    [chosen, entries],
  );
  const set = (key: string, patch: Entry) => {
    setEntries({ ...entries, [key]: { ...entries[key], ...patch } });
    draft.touch();
  };

  return (
    <>
      <DocHeader
        back={{ to: -1, label: 'Quay lại' }}
        plain
        code={editing ? `Sửa phiếu đề xuất ${proposal.number}` : 'Lập phiếu đề xuất'}
        sub="Số lượng đề xuất không vượt số lượng còn lại của hợp đồng"
      />
      {draft.banner}
      <Card style={{ marginBottom: 16 }}>
        <Form
          form={form}
          layout="vertical"
          initialValues={initialHeader}
          onValuesChange={draft.onValuesChange}
        >
          <Row gutter={12}>
            <Col xs={24} md={6}>
              <Form.Item name="departmentId" label="Khoa/phòng" rules={[{ required: true }]}>
                <Select
                  showSearch
                  disabled={editing}
                  optionFilterProp="label"
                  options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={6}>
              <Form.Item name="domain" label="Mảng" rules={[{ required: true }]}>
                <Select disabled={editing} options={DOMAIN_OPTIONS} onChange={() => setEntries({})} />
              </Form.Item>
            </Col>
            <Col xs={24} md={6}>
              <Form.Item name="warehouseId" label="Kho (lấy Xuất – Nhập – Tồn)" rules={[{ required: true }]}>
                <Select
                  disabled={editing}
                  options={(warehouses.data ?? []).map((w) => ({ value: w.id, label: w.name }))}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={6}>
              <Form.Item name="period" label="Kỳ đề xuất" rules={[{ required: true }]}>
                <DatePicker.RangePicker format="DD/MM/YYYY" style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={24}>
              <Form.Item name="note" label="Ghi chú">
                <Input.TextArea rows={2} />
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Card>

      <Card
        title="Mặt hàng có thể đề xuất"
        extra={
          <Space size="large">
            <Statistic title="Số mặt hàng" value={chosen.length} />
            <Statistic title="Giá trị ước tính" value={formatMoney(total)} />
          </Space>
        }
      >
        {candidates.data && !candidates.data.hisAvailable && (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 12 }}
            message={`Không kết nối được ${candidates.data.hisName} — số liệu Xuất – Nhập – Tồn tạm để 0.`}
          />
        )}
        {over.length > 0 && (
          <Alert
            type="error"
            showIcon
            style={{ marginBottom: 12 }}
            message={`${over.length} mặt hàng vượt số lượng còn lại của hợp đồng: ${over.map((r) => `${r.itemName} (còn ${formatQuantity(r.contractRemaining)})`).join('; ')}.`}
          />
        )}
        {!domain || !warehouseId ? (
          <Empty description="Chọn mảng, kho và kỳ để tải danh sách mặt hàng từ các hợp đồng còn hiệu lực" />
        ) : (
          <>
            <Space wrap style={{ marginBottom: 12 }}>
              <Input
                allowClear
                prefix={<SearchOutlined />}
                placeholder="Tìm theo tên hàng, số hợp đồng, nhà cung cấp"
                aria-label="Tìm mặt hàng"
                style={{ width: 360, maxWidth: '100%' }}
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
              <Checkbox checked={onlyChosen} onChange={(e) => setOnlyChosen(e.target.checked)}>
                Chỉ hiện mặt hàng đã nhập số lượng
              </Checkbox>
              {visible.length < rows.length && (
                <Typography.Text type="secondary">
                  Đang hiện {visible.length} / {rows.length} mặt hàng
                </Typography.Text>
              )}
            </Space>
            <Table<ProposalCandidateDto>
              rowKey="contractItemId"
              size="small"
              loading={candidates.isFetching}
              dataSource={visible}
              pagination={false}
              scroll={{ x: 'max-content', y: 560 }}
              rowClassName={(r) =>
                (entries[r.contractItemId]?.qty ?? 0) > r.contractRemaining ? 'row-error' : ''
              }
              onRow={(r) => ({
                style:
                  (entries[r.contractItemId]?.qty ?? 0) > r.contractRemaining
                    ? { background: '#fff1f0' }
                    : undefined,
              })}
              locale={{
                emptyText: (
                  <Empty
                    description={
                      rows.length > 0
                        ? 'Không có mặt hàng nào khớp điều kiện lọc'
                        : 'Không có hợp đồng gọi hàng còn hiệu lực trong mảng này'
                    }
                  />
                ),
              }}
              columns={[
                { title: 'Số HĐ', dataIndex: 'contractNumber', width: 150, fixed: 'left' },
                {
                  title: 'Tên hàng',
                  dataIndex: 'itemName',
                  fixed: 'left',
                  width: 260,
                  render: (v: string, r) => (
                    <Space direction="vertical" size={0}>
                      <span>{v}</span>
                      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                        {r.supplierName}
                      </Typography.Text>
                    </Space>
                  ),
                },
                { title: 'ĐVT', dataIndex: 'unit', width: 60 },
                {
                  title: 'Tồn đầu kỳ',
                  dataIndex: 'openingStock',
                  align: 'right',
                  render: (v: number) => formatQuantity(v),
                },
                {
                  title: 'Nhập',
                  dataIndex: 'received',
                  align: 'right',
                  render: (v: number) => formatQuantity(v),
                },
                {
                  title: 'Xuất',
                  dataIndex: 'issued',
                  align: 'right',
                  render: (v: number) => formatQuantity(v),
                },
                {
                  title: 'Tồn hiện tại',
                  dataIndex: 'currentStock',
                  align: 'right',
                  render: (v: number) => formatQuantity(v),
                },
                {
                  title: 'Còn lại HĐ',
                  dataIndex: 'contractRemaining',
                  align: 'right',
                  render: (v: number) => <Typography.Text strong>{formatQuantity(v)}</Typography.Text>,
                },
                {
                  title: 'SL đề xuất',
                  key: 'qty',
                  width: 150,
                  render: (_, r) => (
                    <QuantityInput
                      size="small"
                      aria-label={`SL đề xuất: ${candidateLabel(r)}`}
                      value={entries[r.contractItemId]?.qty}
                      status={
                        (entries[r.contractItemId]?.qty ?? 0) > r.contractRemaining ? 'error' : undefined
                      }
                      onChange={(v) => set(r.contractItemId, { qty: v ?? undefined })}
                    />
                  ),
                },
                {
                  title: 'Ghi chú',
                  key: 'note',
                  width: 200,
                  render: (_, r) => (
                    <Input
                      size="small"
                      aria-label={`Ghi chú: ${candidateLabel(r)}`}
                      value={entries[r.contractItemId]?.note}
                      onChange={(e) => set(r.contractItemId, { note: e.target.value })}
                    />
                  ),
                },
              ]}
            />
          </>
        )}
      </Card>
      <StickyActionBar total={{ label: 'Tổng giá trị đề xuất', value: formatMoney(total) }}>
        <Button
          type="primary"
          icon={<SaveOutlined />}
          loading={save.isPending}
          disabled={over.length > 0}
          onClick={async () => {
            try {
              const savedId = await save.mutateAsync();
              draft.clear();
              message.success('Đã lưu phiếu đề xuất — kiểm tra rồi Ký và gửi');
              navigate(`/de-xuat/${savedId}`);
            } catch (e) {
              if (e instanceof Error && !('code' in e)) message.error(e.message);
              else onError(e);
            }
          }}
        >
          Lưu phiếu
        </Button>
      </StickyActionBar>
    </>
  );
}
