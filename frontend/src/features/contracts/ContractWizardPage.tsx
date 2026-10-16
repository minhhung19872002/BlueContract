import {
  Alert,
  App,
  Button,
  Card,
  Checkbox,
  Col,
  DatePicker,
  Divider,
  Empty,
  Form,
  Input,
  Radio,
  Row,
  Select,
  Space,
  Switch,
  Table,
  Typography,
} from 'antd';
import { DeleteOutlined, EyeOutlined, PlusOutlined, SaveOutlined } from '@ant-design/icons';
import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import dayjs from '../../i18n/dayjs';
import { useLookup, usePagedList } from '../../api/admin';
import { useTemplates } from '../../api/documents';
import {
  BIDS,
  contractApi,
  useBidDecision,
  useBidItems,
  useContract,
  useContractAction,
  useItemCategories,
  type BidDecisionListItemDto,
  type BidItemDto,
  type ContractDto,
  type ContractKind,
  type ProcurementType,
  type SaveContractRequest,
} from '../../api/procurement';
import { PdfViewer } from '../../components/PdfViewer';
import { statusOptions } from '../../components/StatusTag';
import { useApiError } from '../../components/common';
import { useFormDraft } from '../../hooks/useFormDraft';
import { DocHeader, FormSteps, StickyActionBar } from '../../components/layout';
import { ArrowLeft } from 'lucide-react';
import { MoneyInput, QuantityInput, SupplierSelect } from '../../components/inputs';
import { formatMoney, formatQuantity } from '../../utils/format';
import { TAX_CODE_RULE, USER_NAME_RULE } from '../../utils/validation';

type Day = dayjs.Dayjs | null | undefined;

interface WizardValues {
  procurementType: ProcurementType;
  bidDecisionId?: string;
  supplierId?: string;
  newSupplierMode?: boolean;
  newSupplier?: {
    taxCode: string;
    name: string;
    address?: string;
    representativeName?: string;
    representativeTitle?: string;
    bankAccountNumber?: string;
    bankName?: string;
    notificationEmail?: string;
    phone?: string;
  };
  account?: { create?: boolean; userName?: string; fullName?: string; email?: string };
  categoryId: string;
  number: string;
  name: string;
  kind: ContractKind;
  requiresWarehouseReceipt: boolean;
  facilityIds?: string[];
  note?: string;
  signedDate?: Day;
  effectiveDate?: Day;
  expiryDate?: Day;
  duration?: string;
  paymentTerms?: string;
  warrantyTerms?: string;
  deliveryTerms?: string;
  guaranteeNumber?: string;
  guaranteeBank?: string;
  guaranteeAmount?: number;
  guaranteeExpiry?: Day;
  templateId: string;
  regenerateDocument?: boolean;
}

/** Editable goods line; for bid contracts the ceiling quantity / price come from the decision. */
interface Line {
  key: string;
  itemId?: string;
  code: string;
  name: string;
  unit: string;
  activeIngredient?: string;
  specification?: string;
  manufacturer?: string;
  country?: string;
  quantity: number;
  unitPrice: number;
  vatRate: number;
  maxQuantity?: number;
  maxUnitPrice?: number;
}

const iso = (d: Day) => (d ? d.format('YYYY-MM-DD') : null);
const day = (s?: string | null) => (s ? dayjs(s) : undefined);
let lineSeed = 0;
const newKey = () => `l${Date.now()}-${++lineSeed}`;

function toRequest(v: WizardValues, lines: Line[]): SaveContractRequest {
  return {
    procurementType: v.procurementType,
    bidDecisionId: v.procurementType === 'Bidding' ? (v.bidDecisionId ?? null) : null,
    supplierId: v.newSupplierMode ? null : (v.supplierId ?? null),
    newSupplier:
      v.newSupplierMode && v.newSupplier
        ? {
            ...v.newSupplier,
            shortName: null,
            authorizationDocument: null,
            address: v.newSupplier.address ?? null,
            representativeName: v.newSupplier.representativeName ?? null,
            representativeTitle: v.newSupplier.representativeTitle ?? null,
            bankAccountNumber: v.newSupplier.bankAccountNumber ?? null,
            bankName: v.newSupplier.bankName ?? null,
            notificationEmail: v.newSupplier.notificationEmail ?? null,
            phone: v.newSupplier.phone ?? null,
            isActive: true,
            adminAccount: v.account?.create
              ? {
                  userName: v.account.userName ?? '',
                  fullName: v.account.fullName ?? '',
                  email: v.account.email ?? '',
                }
              : null,
          }
        : null,
    number: v.number,
    name: v.name,
    kind: v.kind,
    requiresWarehouseReceipt: v.requiresWarehouseReceipt,
    categoryId: v.categoryId,
    facilityIds: v.facilityIds ?? [],
    note: v.note ?? null,
    signedDate: iso(v.signedDate),
    effectiveDate: iso(v.effectiveDate),
    expiryDate: iso(v.expiryDate),
    duration: v.duration ?? null,
    paymentTerms: v.paymentTerms ?? null,
    warrantyTerms: v.warrantyTerms ?? null,
    deliveryTerms: v.deliveryTerms ?? null,
    guaranteeNumber: v.guaranteeNumber ?? null,
    guaranteeBank: v.guaranteeBank ?? null,
    guaranteeAmount: v.guaranteeAmount ?? null,
    guaranteeExpiry: iso(v.guaranteeExpiry),
    templateId: v.templateId,
    regenerateDocument: v.regenerateDocument ?? true,
    items: lines.map((l) => ({
      itemId: l.itemId ?? null,
      newItem: l.itemId
        ? null
        : {
            code: l.code,
            name: l.name,
            unit: l.unit,
            activeIngredient: l.activeIngredient ?? null,
            specification: l.specification ?? null,
            manufacturer: l.manufacturer ?? null,
            country: l.country ?? null,
            hisKey: null,
          },
      quantity: l.quantity,
      unitPrice: l.unitPrice,
      vatRate: l.vatRate,
    })),
  };
}

function fromContract(c: ContractDto): { values: Partial<WizardValues>; lines: Line[] } {
  return {
    values: {
      procurementType: c.procurementType,
      bidDecisionId: c.bidDecisionId ?? undefined,
      supplierId: c.supplierId,
      categoryId: c.categoryId,
      number: c.number,
      name: c.name,
      kind: c.kind,
      requiresWarehouseReceipt: c.requiresWarehouseReceipt,
      facilityIds: c.facilityIds,
      note: c.note ?? undefined,
      signedDate: day(c.signedDate),
      effectiveDate: day(c.effectiveDate),
      expiryDate: day(c.expiryDate),
      duration: c.duration ?? undefined,
      paymentTerms: c.paymentTerms ?? undefined,
      warrantyTerms: c.warrantyTerms ?? undefined,
      deliveryTerms: c.deliveryTerms ?? undefined,
      guaranteeNumber: c.guaranteeNumber ?? undefined,
      guaranteeBank: c.guaranteeBank ?? undefined,
      guaranteeAmount: c.guaranteeAmount ?? undefined,
      guaranteeExpiry: day(c.guaranteeExpiry),
      templateId: c.templateId ?? undefined,
      regenerateDocument: true,
    },
    lines: c.items.map((i) => ({
      key: i.itemId,
      itemId: i.itemId,
      code: i.itemCode,
      name: i.itemName,
      unit: i.unit,
      quantity: i.quantity,
      unitPrice: i.unitPrice,
      vatRate: i.vatRate,
      maxUnitPrice: i.maxUnitPrice ?? undefined,
    })),
  };
}

/** Bước 2 (trong thầu): chọn mặt hàng trúng thầu của NCC, SL ≤ còn lại, đơn giá ≤ giá trúng thầu. */
function BidLines({
  bidId,
  supplierId,
  contractId,
  lines,
  setLines,
  defaultVat,
}: {
  bidId: string;
  supplierId: string;
  contractId?: string;
  lines: Line[];
  setLines: (l: Line[]) => void;
  defaultVat: number;
}) {
  const items = useBidItems(bidId, supplierId);
  const selected = new Map(lines.map((l) => [l.itemId!, l]));
  const remaining = (r: BidItemDto) =>
    r.remainingQuantity + (contractId ? (selected.get(r.itemId)?.quantity ?? 0) : 0);

  const toggle = (r: BidItemDto, on: boolean) =>
    setLines(
      on
        ? [
            ...lines,
            {
              key: r.itemId,
              itemId: r.itemId,
              code: r.code,
              name: r.name,
              unit: r.unit,
              quantity: remaining(r),
              unitPrice: r.unitPrice,
              vatRate: defaultVat,
              maxQuantity: remaining(r),
              maxUnitPrice: r.unitPrice,
            },
          ]
        : lines.filter((l) => l.itemId !== r.itemId),
    );
  const patch = (itemId: string, p: Partial<Line>) =>
    setLines(lines.map((l) => (l.itemId === itemId ? { ...l, ...p } : l)));

  return (
    <Table<BidItemDto>
      rowKey="itemId"
      size="small"
      loading={items.isLoading}
      dataSource={items.data ?? []}
      pagination={false}
      scroll={{ x: 'max-content', y: 520 }}
      rowSelection={{
        selectedRowKeys: [...selected.keys()],
        onSelect: (r, on) => toggle(r, on),
        onSelectAll: (on, _rows, changed) => {
          if (on) {
            setLines([
              ...lines,
              ...changed
                .filter((r) => remaining(r) > 0)
                .map((r) => ({
                  key: r.itemId,
                  itemId: r.itemId,
                  code: r.code,
                  name: r.name,
                  unit: r.unit,
                  quantity: remaining(r),
                  unitPrice: r.unitPrice,
                  vatRate: defaultVat,
                  maxQuantity: remaining(r),
                  maxUnitPrice: r.unitPrice,
                })),
            ]);
          } else {
            setLines([]);
          }
        },
        getCheckboxProps: (r) => ({ disabled: remaining(r) <= 0 && !selected.has(r.itemId) }),
      }}
      columns={[
        { title: 'Mã', dataIndex: 'code', width: 90 },
        {
          title: 'Tên hàng',
          dataIndex: 'name',
          render: (v: string, r) => (
            <Space direction="vertical" size={0}>
              <span>{v}</span>
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                {[r.activeIngredient, r.strength, r.specification].filter(Boolean).join(' · ')}
              </Typography.Text>
            </Space>
          ),
        },
        { title: 'ĐVT', dataIndex: 'unit', width: 60 },
        {
          title: 'Còn lại',
          key: 'remaining',
          align: 'right',
          width: 110,
          render: (_, r) => formatQuantity(remaining(r)),
        },
        {
          title: 'Giá trúng thầu',
          dataIndex: 'unitPrice',
          align: 'right',
          width: 120,
          render: (v: number) => formatMoney(v),
        },
        {
          title: 'SL hợp đồng',
          key: 'qty',
          width: 150,
          render: (_, r) => {
            const l = selected.get(r.itemId);
            return l ? (
              <QuantityInput
                size="small"
                value={l.quantity}
                max={remaining(r)}
                status={l.quantity > remaining(r) || l.quantity <= 0 ? 'error' : undefined}
                onChange={(v) => patch(r.itemId, { quantity: v ?? 0 })}
              />
            ) : null;
          },
        },
        {
          title: 'Đơn giá HĐ',
          key: 'price',
          width: 170,
          render: (_, r) => {
            const l = selected.get(r.itemId);
            return l ? (
              <MoneyInput
                size="small"
                value={l.unitPrice}
                max={r.unitPrice}
                status={l.unitPrice > r.unitPrice ? 'error' : undefined}
                onChange={(v) => patch(r.itemId, { unitPrice: v ?? 0 })}
              />
            ) : null;
          },
        },
        {
          title: 'VAT %',
          key: 'vat',
          width: 90,
          render: (_, r) => {
            const l = selected.get(r.itemId);
            return l ? (
              <QuantityInput
                size="small"
                value={l.vatRate}
                max={100}
                precision={0}
                onChange={(v) => patch(r.itemId, { vatRate: v ?? 0 })}
              />
            ) : null;
          },
        },
        {
          title: 'Thành tiền',
          key: 'amount',
          align: 'right',
          width: 140,
          render: (_, r) => {
            const l = selected.get(r.itemId);
            return l ? formatMoney(Math.round(l.quantity * l.unitPrice)) : '';
          },
        },
      ]}
    />
  );
}

/** Bước 2 (ngoài thầu): nhập mặt hàng / dịch vụ trực tiếp. */
function FreeLines({
  lines,
  setLines,
  defaultVat,
}: {
  lines: Line[];
  setLines: (l: Line[]) => void;
  defaultVat: number;
}) {
  const patch = (key: string, p: Partial<Line>) =>
    setLines(lines.map((l) => (l.key === key ? { ...l, ...p } : l)));
  return (
    <>
      <Table<Line>
        rowKey="key"
        size="small"
        dataSource={lines}
        pagination={false}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: <Empty description="Chưa có mặt hàng — bấm Thêm dòng" /> }}
        columns={[
          {
            title: 'Mã',
            key: 'code',
            width: 120,
            render: (_, l) => (
              <Input
                size="small"
                value={l.code}
                disabled={!!l.itemId}
                onChange={(e) => patch(l.key, { code: e.target.value })}
              />
            ),
          },
          {
            title: 'Tên hàng hóa / dịch vụ',
            key: 'name',
            width: 280,
            render: (_, l) => (
              <Input
                size="small"
                value={l.name}
                disabled={!!l.itemId}
                onChange={(e) => patch(l.key, { name: e.target.value })}
              />
            ),
          },
          {
            title: 'Thông số / mô tả',
            key: 'spec',
            width: 220,
            render: (_, l) => (
              <Input
                size="small"
                value={l.activeIngredient}
                disabled={!!l.itemId}
                onChange={(e) => patch(l.key, { activeIngredient: e.target.value })}
              />
            ),
          },
          {
            title: 'ĐVT',
            key: 'unit',
            width: 80,
            render: (_, l) => (
              <Input
                size="small"
                value={l.unit}
                disabled={!!l.itemId}
                onChange={(e) => patch(l.key, { unit: e.target.value })}
              />
            ),
          },
          {
            title: 'Số lượng',
            key: 'qty',
            width: 130,
            render: (_, l) => (
              <QuantityInput
                size="small"
                value={l.quantity}
                onChange={(v) => patch(l.key, { quantity: v ?? 0 })}
              />
            ),
          },
          {
            title: 'Đơn giá (gồm VAT)',
            key: 'price',
            width: 170,
            render: (_, l) => (
              <MoneyInput
                size="small"
                value={l.unitPrice}
                onChange={(v) => patch(l.key, { unitPrice: v ?? 0 })}
              />
            ),
          },
          {
            title: 'VAT %',
            key: 'vat',
            width: 90,
            render: (_, l) => (
              <QuantityInput
                size="small"
                value={l.vatRate}
                max={100}
                precision={0}
                onChange={(v) => patch(l.key, { vatRate: v ?? 0 })}
              />
            ),
          },
          {
            title: 'Thành tiền',
            key: 'amount',
            align: 'right',
            width: 140,
            render: (_, l) => formatMoney(Math.round(l.quantity * l.unitPrice)),
          },
          {
            title: '',
            key: 'del',
            width: 50,
            render: (_, l) => (
              <Button
                size="small"
                danger
                icon={<DeleteOutlined />}
                onClick={() => setLines(lines.filter((x) => x.key !== l.key))}
              />
            ),
          },
        ]}
      />
      <Button
        type="dashed"
        block
        icon={<PlusOutlined />}
        style={{ marginTop: 8 }}
        onClick={() =>
          setLines([
            ...lines,
            { key: newKey(), code: '', name: '', unit: '', quantity: 1, unitPrice: 0, vatRate: defaultVat },
          ])
        }
      >
        Thêm dòng
      </Button>
    </>
  );
}

/** Tạo / sửa hợp đồng — wizard 4 bước (spec §9.1-5). Waits for the contract when editing, then mounts the wizard. */
export function ContractWizardPage() {
  const { id } = useParams<{ id: string }>();
  const [params] = useSearchParams();
  const existing = useContract(id);
  if (id && !existing.data) return <Card loading />;
  const bidParam = params.get('bid');
  const init = existing.data
    ? fromContract(existing.data)
    : {
        values: {
          procurementType: 'Bidding' as ProcurementType,
          bidDecisionId: bidParam ?? undefined,
          supplierId: params.get('supplier') ?? undefined,
          kind: 'UnitPriceCallOff' as ContractKind,
          requiresWarehouseReceipt: true,
          regenerateDocument: true,
          account: { create: true },
        },
        lines: [],
      };
  return (
    <ContractWizard
      key={id ?? 'new'}
      contractId={id}
      contractNumber={existing.data?.number}
      initialValues={init.values}
      initialLines={init.lines}
      offerDraft={!id && !bidParam}
    />
  );
}

function ContractWizard({
  contractId: id,
  contractNumber,
  initialValues,
  initialLines,
  offerDraft,
}: {
  contractId?: string;
  contractNumber?: string;
  initialValues: Partial<WizardValues>;
  initialLines: Line[];
  offerDraft: boolean;
}) {
  const editing = !!id;
  const navigate = useNavigate();
  const { message } = App.useApp();
  const onError = useApiError();
  const [form] = Form.useForm<WizardValues>();
  const [step, setStep] = useState(0);
  const [lines, setLines] = useState<Line[]>(initialLines);
  const [previewBlob, setPreviewBlob] = useState<{ blob: Blob; at: number } | null>(null);
  const [previewing, setPreviewing] = useState(false);
  // Lưu nháp tự động (spec §9.3) for a new contract; lines are edited outside the antd form.
  const draft = useFormDraft({
    key: offerDraft ? 'contract-new' : null,
    form,
    extra: lines,
    onRestoreExtra: setLines,
  });
  const editLines = (next: Line[]) => {
    setLines(next);
    draft.touch();
  };
  const categories = useItemCategories();
  const facilities = useLookup('facilities');
  const templates = useTemplates('Contract');
  const bids = usePagedList<BidDecisionListItemDto>(BIDS, { status: 'Active', pageSize: 200 });

  const procurementType = Form.useWatch('procurementType', form);
  const bidDecisionId = Form.useWatch('bidDecisionId', form);
  const supplierId = Form.useWatch('supplierId', form);
  const newSupplierMode = Form.useWatch('newSupplierMode', form);
  const categoryId = Form.useWatch('categoryId', form);
  const createAccount = Form.useWatch(['account', 'create'], form);
  const bid = useBidDecision(procurementType === 'Bidding' ? bidDecisionId : undefined);
  const category = categories.data?.find((c) => c.id === categoryId);
  const defaultVat = category?.importKind === 'General' ? 10 : 5;

  const save = useContractAction((v: { values: WizardValues; lines: Line[] }) =>
    editing
      ? contractApi.update(id!, toRequest(v.values, v.lines)).then(() => id!)
      : contractApi.create(toRequest(v.values, v.lines)),
  );

  // Defaults derived from the chosen bid decision.
  useEffect(() => {
    if (!bid.data || editing) return;
    const current = form.getFieldsValue();
    form.setFieldsValue({
      categoryId: current.categoryId ?? bid.data.categoryId,
      name: current.name || bid.data.packageName.replace(/^Gói thầu số \d+:\s*/i, ''),
      duration: current.duration || bid.data.executionPeriod || undefined,
      facilityIds: current.facilityIds?.length
        ? current.facilityIds
        : bid.data.facilityId
          ? [bid.data.facilityId]
          : [],
    });
  }, [bid.data, editing, form]);

  // Default template for the category.
  useEffect(() => {
    if (!category || form.getFieldValue('templateId')) return;
    const t = templates.data?.find((x) => x.code === category.defaultContractTemplateCode && x.isActive);
    if (t)
      form.setFieldsValue({
        templateId: t.id,
        requiresWarehouseReceipt:
          category.importKind === 'Medical' || category.code === 'TAI_SAN' || category.code === 'HANG_HOA',
      });
  }, [category, templates.data, form]);

  const total = useMemo(() => lines.reduce((s, l) => s + Math.round(l.quantity * l.unitPrice), 0), [lines]);
  const vat = useMemo(
    () =>
      lines.reduce(
        (s, l) => s + Math.round((Math.round(l.quantity * l.unitPrice) * l.vatRate) / (100 + l.vatRate)),
        0,
      ),
    [lines],
  );

  const stepFields: (keyof WizardValues | string[])[][] = [
    [
      'procurementType',
      'bidDecisionId',
      'supplierId',
      'categoryId',
      ['newSupplier', 'taxCode'],
      ['newSupplier', 'name'],
      ['account', 'userName'],
      ['account', 'fullName'],
      ['account', 'email'],
    ],
    [],
    ['number', 'name', 'kind', 'effectiveDate', 'expiryDate'],
    ['templateId'],
  ];

  const next = async () => {
    try {
      await form.validateFields(stepFields[step] as never);
    } catch {
      return;
    }

    if (step === 1) {
      if (lines.length === 0) {
        message.warning('Chọn hoặc nhập ít nhất một mặt hàng.');
        return;
      }

      const bad = lines.find(
        (l) =>
          l.quantity <= 0 ||
          (l.maxQuantity !== undefined && l.quantity > l.maxQuantity) ||
          (l.maxUnitPrice !== undefined && l.unitPrice > l.maxUnitPrice) ||
          !l.code ||
          !l.name ||
          !l.unit,
      );
      if (bad) {
        message.error(
          `Dòng "${bad.name || bad.code || 'mới'}" chưa hợp lệ: kiểm tra mã, tên, ĐVT, số lượng (≤ còn lại) và đơn giá (≤ giá trúng thầu).`,
        );
        return;
      }
    }

    draft.touch();
    setStep(step + 1);
  };

  const preview = async () => {
    const v = form.getFieldsValue(true) as WizardValues;
    if (v.newSupplierMode) {
      message.info('Xem trước cần NCC đã có trong hệ thống — hãy lưu hợp đồng để sinh tài liệu với NCC mới.');
      return;
    }

    setPreviewing(true);
    try {
      setPreviewBlob({ blob: await contractApi.preview(toRequest(v, lines)), at: Date.now() });
    } catch (e) {
      onError(e);
    } finally {
      setPreviewing(false);
    }
  };

  const submit = async () => {
    const v = form.getFieldsValue(true) as WizardValues;
    try {
      await form.validateFields();
    } catch {
      message.error('Còn trường bắt buộc chưa nhập ở các bước trước.');
      return;
    }

    try {
      const contractId = await save.mutateAsync({ values: v, lines });
      draft.clear();
      message.success(editing ? 'Đã lưu hợp đồng' : 'Đã tạo hợp đồng và sinh tài liệu từ mẫu');
      navigate(`/hop-dong/${contractId}`);
    } catch (e) {
      onError(e);
    }
  };

  const supplierOptions = (bid.data?.suppliers ?? []).map((s) => ({
    value: s.supplierId,
    label: `${s.taxCode} — ${s.name}`,
  }));

  return (
    <>
      <DocHeader
        back={{ to: -1, label: 'Quay lại' }}
        plain
        code={editing ? `Sửa hợp đồng ${contractNumber ?? ''}` : 'Tạo hợp đồng'}
        sub="Chọn nguồn, nhà cung cấp và hàng hóa, nhập điều khoản rồi xem trước hợp đồng sinh từ mẫu"
      />
      {draft.banner}
      <FormSteps
        steps={['Nguồn', 'NCC & hàng hóa', 'Điều khoản', 'Mẫu & xem trước']}
        current={step}
        onChange={setStep}
      />
      <Card>
        <Form
          form={form}
          layout="vertical"
          initialValues={initialValues}
          onValuesChange={draft.onValuesChange}
        >
          {/* Step 0: source */}
          <div style={{ display: step === 0 ? undefined : 'none' }}>
            <Form.Item name="procurementType" label="Hình thức">
              <Radio.Group
                disabled={editing}
                options={statusOptions('procurementType')}
                optionType="button"
                buttonStyle="solid"
                onChange={() => {
                  setLines([]);
                  form.setFieldsValue({
                    bidDecisionId: undefined,
                    supplierId: undefined,
                    newSupplierMode: false,
                  });
                }}
              />
            </Form.Item>
            {procurementType === 'Bidding' ? (
              <Row gutter={12}>
                <Col xs={24} md={12}>
                  <Form.Item
                    name="bidDecisionId"
                    label="Quyết định thầu (đã ban hành)"
                    rules={[{ required: true, message: 'Chọn quyết định thầu' }]}
                  >
                    <Select
                      showSearch
                      disabled={editing}
                      optionFilterProp="label"
                      loading={bids.isLoading}
                      options={(bids.data?.items ?? []).map((b) => ({
                        value: b.id,
                        label: `${b.number} — ${b.packageName}`,
                      }))}
                      onChange={() => {
                        setLines([]);
                        form.setFieldsValue({
                          supplierId: undefined,
                          categoryId: undefined,
                          templateId: undefined,
                        });
                      }}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item
                    name="supplierId"
                    label="Nhà cung cấp trúng thầu"
                    rules={[{ required: true, message: 'Chọn nhà cung cấp' }]}
                  >
                    <Select
                      disabled={editing || !bidDecisionId}
                      options={supplierOptions}
                      onChange={() => setLines([])}
                    />
                  </Form.Item>
                </Col>
              </Row>
            ) : (
              <>
                <Form.Item name="newSupplierMode" valuePropName="checked">
                  <Checkbox disabled={editing}>
                    Nhà cung cấp mới (tạo ngay, cấp tài khoản và gửi email kích hoạt)
                  </Checkbox>
                </Form.Item>
                {newSupplierMode ? (
                  <Row gutter={12}>
                    <Col xs={24} md={8}>
                      <Form.Item
                        name={['newSupplier', 'taxCode']}
                        label="Mã số thuế"
                        rules={[{ required: true }, TAX_CODE_RULE]}
                      >
                        <Input />
                      </Form.Item>
                    </Col>
                    <Col xs={24} md={16}>
                      <Form.Item
                        name={['newSupplier', 'name']}
                        label="Tên nhà cung cấp"
                        rules={[{ required: true, whitespace: true }]}
                      >
                        <Input />
                      </Form.Item>
                    </Col>
                    <Col xs={24} md={12}>
                      <Form.Item name={['newSupplier', 'address']} label="Địa chỉ">
                        <Input />
                      </Form.Item>
                    </Col>
                    <Col xs={12} md={6}>
                      <Form.Item name={['newSupplier', 'representativeName']} label="Người đại diện">
                        <Input />
                      </Form.Item>
                    </Col>
                    <Col xs={12} md={6}>
                      <Form.Item name={['newSupplier', 'representativeTitle']} label="Chức vụ">
                        <Input />
                      </Form.Item>
                    </Col>
                    <Col xs={12} md={6}>
                      <Form.Item name={['newSupplier', 'bankAccountNumber']} label="Số tài khoản">
                        <Input />
                      </Form.Item>
                    </Col>
                    <Col xs={12} md={6}>
                      <Form.Item name={['newSupplier', 'bankName']} label="Ngân hàng">
                        <Input />
                      </Form.Item>
                    </Col>
                    <Col xs={12} md={6}>
                      <Form.Item
                        name={['newSupplier', 'notificationEmail']}
                        label="Email nhận thông báo"
                        rules={[{ type: 'email' }]}
                      >
                        <Input />
                      </Form.Item>
                    </Col>
                    <Col xs={12} md={6}>
                      <Form.Item name={['newSupplier', 'phone']} label="Điện thoại">
                        <Input />
                      </Form.Item>
                    </Col>
                    <Col span={24}>
                      <Form.Item name={['account', 'create']} valuePropName="checked">
                        <Checkbox>Cấp tài khoản quản trị NCC (gửi mật khẩu tạm qua email)</Checkbox>
                      </Form.Item>
                    </Col>
                    {createAccount && (
                      <>
                        <Col xs={24} md={8}>
                          <Form.Item
                            name={['account', 'userName']}
                            label="Tên đăng nhập"
                            rules={[{ required: true }, USER_NAME_RULE]}
                          >
                            <Input />
                          </Form.Item>
                        </Col>
                        <Col xs={24} md={8}>
                          <Form.Item
                            name={['account', 'fullName']}
                            label="Họ tên"
                            rules={[{ required: true, whitespace: true }]}
                          >
                            <Input />
                          </Form.Item>
                        </Col>
                        <Col xs={24} md={8}>
                          <Form.Item
                            name={['account', 'email']}
                            label="Email"
                            rules={[{ required: true, type: 'email' }]}
                          >
                            <Input />
                          </Form.Item>
                        </Col>
                      </>
                    )}
                  </Row>
                ) : (
                  <Form.Item
                    name="supplierId"
                    label="Nhà cung cấp"
                    rules={[{ required: true, message: 'Chọn nhà cung cấp' }]}
                  >
                    <SupplierSelect disabled={editing} />
                  </Form.Item>
                )}
              </>
            )}
            <Row gutter={12}>
              <Col xs={24} md={12}>
                <Form.Item name="categoryId" label="Hạng mục" rules={[{ required: true }]}>
                  <Select
                    disabled={procurementType === 'Bidding'}
                    options={(categories.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
                    onChange={() => form.setFieldsValue({ templateId: undefined })}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} md={12}>
                <Form.Item
                  name="facilityIds"
                  label="Cơ sở thực hiện"
                  extra="Để trống = toàn bệnh viện; chọn được nhiều cơ sở."
                >
                  <Select
                    mode="multiple"
                    allowClear
                    placeholder="Toàn bệnh viện"
                    optionFilterProp="label"
                    options={(facilities.data ?? []).map((f) => ({ value: f.id, label: f.name }))}
                  />
                </Form.Item>
              </Col>
            </Row>
          </div>

          {/* Step 1: goods */}
          <div style={{ display: step === 1 ? undefined : 'none' }}>
            {procurementType === 'Bidding' && bidDecisionId && supplierId ? (
              <BidLines
                bidId={bidDecisionId}
                supplierId={supplierId}
                contractId={id}
                lines={lines}
                setLines={editLines}
                defaultVat={defaultVat}
              />
            ) : procurementType === 'NonBidding' ? (
              <FreeLines lines={lines} setLines={editLines} defaultVat={defaultVat} />
            ) : (
              <Empty description="Chọn quyết định thầu và nhà cung cấp ở bước trước" />
            )}
          </div>

          {/* Step 2: terms */}
          <div style={{ display: step === 2 ? undefined : 'none' }}>
            <Row gutter={12}>
              <Col xs={24} md={8}>
                <Form.Item name="number" label="Số hợp đồng" rules={[{ required: true, whitespace: true }]}>
                  <Input placeholder="VD: 15/2026/HĐ-BVĐK" />
                </Form.Item>
              </Col>
              <Col xs={24} md={16}>
                <Form.Item name="name" label="Tên hợp đồng" rules={[{ required: true, whitespace: true }]}>
                  <Input />
                </Form.Item>
              </Col>
              <Col xs={24} md={8}>
                <Form.Item name="kind" label="Loại hợp đồng" rules={[{ required: true }]}>
                  <Select options={statusOptions('contractKind')} />
                </Form.Item>
              </Col>
              <Col xs={24} md={8}>
                <Form.Item
                  name="requiresWarehouseReceipt"
                  label="Có nhập kho"
                  valuePropName="checked"
                  extra="Tắt với dịch vụ/sửa chữa: nghiệm thu bằng phiếu xác nhận khối lượng"
                >
                  <Switch />
                </Form.Item>
              </Col>
              <Col xs={24} md={8}>
                <Form.Item name="duration" label="Thời gian thực hiện">
                  <Input />
                </Form.Item>
              </Col>
              <Col xs={8}>
                <Form.Item name="signedDate" label="Ngày ký (để trống: ngày ký số cuối)">
                  <DatePicker format="DD/MM/YYYY" style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col xs={8}>
                <Form.Item name="effectiveDate" label="Ngày hiệu lực">
                  <DatePicker format="DD/MM/YYYY" style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col xs={8}>
                <Form.Item
                  name="expiryDate"
                  label="Ngày hết hạn"
                  dependencies={['effectiveDate']}
                  rules={[
                    ({ getFieldValue }) => ({
                      validator: (_, v: Day) =>
                        !v || !getFieldValue('effectiveDate') || !v.isBefore(getFieldValue('effectiveDate'))
                          ? Promise.resolve()
                          : Promise.reject(new Error('Ngày hết hạn phải sau ngày hiệu lực')),
                    }),
                  ]}
                >
                  <DatePicker format="DD/MM/YYYY" style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col xs={24} md={12}>
                <Form.Item name="paymentTerms" label="Điều khoản thanh toán">
                  <Input.TextArea rows={3} />
                </Form.Item>
              </Col>
              <Col xs={24} md={12}>
                <Form.Item name="deliveryTerms" label="Điều khoản giao hàng">
                  <Input.TextArea rows={3} />
                </Form.Item>
              </Col>
              <Col xs={24} md={12}>
                <Form.Item name="warrantyTerms" label="Bảo hành / hạn dùng">
                  <Input.TextArea rows={2} />
                </Form.Item>
              </Col>
              <Col xs={24} md={12}>
                <Form.Item name="note" label="Ghi chú">
                  <Input.TextArea rows={2} />
                </Form.Item>
              </Col>
            </Row>
            <Divider orientation="left">Bảo lãnh thực hiện hợp đồng</Divider>
            <Row gutter={12}>
              <Col xs={12} md={6}>
                <Form.Item name="guaranteeNumber" label="Số bảo lãnh">
                  <Input />
                </Form.Item>
              </Col>
              <Col xs={12} md={6}>
                <Form.Item name="guaranteeBank" label="Ngân hàng phát hành">
                  <Input />
                </Form.Item>
              </Col>
              <Col xs={12} md={6}>
                <Form.Item name="guaranteeAmount" label="Giá trị bảo lãnh">
                  <MoneyInput />
                </Form.Item>
              </Col>
              <Col xs={12} md={6}>
                <Form.Item name="guaranteeExpiry" label="Hiệu lực đến">
                  <DatePicker format="DD/MM/YYYY" style={{ width: '100%' }} />
                </Form.Item>
              </Col>
            </Row>
          </div>

          {/* Step 3: template & preview */}
          <div style={{ display: step === 3 ? undefined : 'none' }}>
            <Row gutter={12} align="bottom">
              <Col xs={24} md={14}>
                <Form.Item
                  name="templateId"
                  label="Mẫu hợp đồng"
                  rules={[{ required: true, message: 'Chọn mẫu hợp đồng' }]}
                >
                  <Select
                    loading={templates.isLoading}
                    options={(templates.data ?? [])
                      .filter((t) => t.isActive)
                      .map((t) => ({ value: t.id, label: t.name }))}
                    onChange={() => setPreviewBlob(null)}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} md={10}>
                <Form.Item>
                  <Button icon={<EyeOutlined />} loading={previewing} onClick={preview}>
                    Xem trước PDF
                  </Button>
                </Form.Item>
              </Col>
            </Row>
            {editing && (
              <Form.Item name="regenerateDocument" valuePropName="checked">
                <Checkbox>
                  Sinh lại tài liệu từ mẫu (tạo phiên bản mới — các chỉnh sửa trực tiếp trên DOCX trước đó sẽ
                  được thay thế)
                </Checkbox>
              </Form.Item>
            )}
            {previewBlob ? (
              <PdfViewer
                load={() => Promise.resolve(previewBlob.blob)}
                cacheKey={`preview-${previewBlob.at}`}
                height="65vh"
              />
            ) : (
              <Alert
                type="info"
                showIcon
                message="Bấm Xem trước PDF để kiểm tra hợp đồng sinh từ mẫu trước khi lưu. Sau khi lưu có thể chỉnh sửa trực tiếp file DOCX trong trình soạn thảo."
              />
            )}
          </div>
        </Form>
      </Card>
      <StickyActionBar
        total={{
          label: 'Tổng giá trị (gồm VAT)',
          value: formatMoney(total),
          sub: `Số mặt hàng: ${lines.length} · Thuế GTGT ${formatMoney(vat)}`,
        }}
      >
        {step > 0 && (
          <Button icon={<ArrowLeft size={16} aria-hidden />} onClick={() => setStep(step - 1)}>
            Quay lại
          </Button>
        )}
        {step < 3 ? (
          <Button type="primary" onClick={next}>
            Tiếp tục
          </Button>
        ) : (
          <Button type="primary" icon={<SaveOutlined />} loading={save.isPending} onClick={submit}>
            {editing ? 'Lưu hợp đồng' : 'Tạo hợp đồng'}
          </Button>
        )}
      </StickyActionBar>
    </>
  );
}
