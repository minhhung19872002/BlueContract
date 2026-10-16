import { App, Button, Input, InputNumber, Select, Space, Switch, Table, Tag, Tooltip } from 'antd';
import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { ArrowUp, ChevronRight, GripVertical, Info, Plus, Save, Trash2 } from 'lucide-react';
import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core';
import {
  SortableContext,
  arrayMove,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useMemo, useState } from 'react';
import { useLookup } from '../../api/admin';
import {
  useSaveWorkflowTemplate,
  useWorkflowTemplates,
  type AssigneeType,
  type DocumentType,
  type WorkflowActionType,
  type WorkflowTemplateDto,
} from '../../api/signing';
import { StatusTag, statusLabel, statusOptions } from '../../components/StatusTag';
import { PageHeader, useApiError } from '../../components/common';
import { Banner, DocHeader, Panel } from '../../components/layout';
import './workflowBuilder.css';

const CREATOR = '__creator';

/** Editable step; return targets are kept as step keys so they survive drag-and-drop reordering. */
interface StepDraft {
  key: string;
  code: string;
  name: string;
  actionType: WorkflowActionType;
  assigneeType: AssigneeType;
  roleCode?: string | null;
  userId?: string | null;
  allowEdit: boolean;
  isOptional: boolean;
  returnTo: string[];
  signatureAnchor?: string | null;
  slaHours?: number | null;
}

interface Draft {
  id?: string;
  code: string;
  name: string;
  documentType: DocumentType;
  condition?: string | null;
  isDefault: boolean;
  isActive: boolean;
  steps: StepDraft[];
}

let keySeed = 0;
const newKey = () => `s${++keySeed}`;

function toDraft(t?: WorkflowTemplateDto): Draft {
  if (!t) {
    return { code: '', name: '', documentType: 'Contract', isDefault: false, isActive: true, steps: [] };
  }
  const keys = t.steps.map(() => newKey());
  return {
    id: t.id,
    code: t.code,
    name: t.name,
    documentType: t.documentType,
    condition: t.condition,
    isDefault: t.isDefault,
    isActive: t.isActive,
    steps: t.steps.map((s, i) => ({
      key: keys[i]!,
      code: s.code,
      name: s.name,
      actionType: s.actionType,
      assigneeType: s.assigneeType,
      roleCode: s.roleCode,
      userId: s.userId,
      allowEdit: s.allowEdit,
      isOptional: s.isOptional,
      returnTo: s.returnTo.map((o) => (o === 0 ? CREATOR : keys[o - 1]!)).filter(Boolean),
      signatureAnchor: s.signatureAnchor,
      slaHours: s.slaHours,
    })),
  };
}

function SortableStep({
  step,
  index,
  previous,
  roles,
  users,
  onChange,
  onRemove,
  onUp,
}: {
  step: StepDraft;
  index: number;
  previous: StepDraft[];
  roles: { value: string; label: string }[];
  users: { value: string; label: string }[];
  onChange: (patch: Partial<StepDraft>) => void;
  onRemove: () => void;
  onUp?: () => void;
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: step.key,
  });
  return (
    <div
      ref={setNodeRef}
      className={`wfb-step${isDragging ? ' is-dragging' : ''}`}
      style={{ transform: CSS.Transform.toString(transform), transition }}
    >
      <div className="wfb-step__handle">
        <button
          type="button"
          className="wfb-step__grip"
          {...attributes}
          {...listeners}
          aria-label={`Kéo để đổi thứ tự bước ${index + 1}`}
        >
          <GripVertical size={18} aria-hidden />
        </button>
        <span className="wfb-step__number">{index + 1}</span>
      </div>
      <div className="wfb-step__fields">
        <label className="wfb-field wfb-field--wide">
          <span className="wfb-field__label">Tên bước</span>
          <Input value={step.name} onChange={(e) => onChange({ name: e.target.value })} />
        </label>
        <label className="wfb-field">
          <span className="wfb-field__label">Mã bước</span>
          <Input
            className="mono"
            value={step.code}
            onChange={(e) => onChange({ code: e.target.value.toUpperCase() })}
          />
        </label>
        <div className="wfb-field">
          <span className="wfb-field__label">Kiểu bước</span>
          <Select
            aria-label="Kiểu bước"
            value={step.actionType}
            options={statusOptions('workflowAction').map((o) => ({
              ...o,
              label: <StatusTag domain="workflowAction" value={o.value} />,
            }))}
            onChange={(v) => onChange({ actionType: v as WorkflowActionType })}
          />
        </div>
        <div className="wfb-field">
          <span className="wfb-field__label">Người được giao</span>
          <Select
            aria-label="Cách giao"
            value={step.assigneeType}
            options={statusOptions('assigneeType')}
            onChange={(v) => onChange({ assigneeType: v as AssigneeType, roleCode: null, userId: null })}
          />
        </div>
        {step.assigneeType === 'Role' && (
          <div className="wfb-field">
            <span className="wfb-field__label">Vai trò</span>
            <Select
              aria-label="Vai trò"
              placeholder="Chọn vai trò"
              showSearch
              optionFilterProp="label"
              value={step.roleCode ?? undefined}
              options={roles}
              onChange={(v) => onChange({ roleCode: v })}
            />
          </div>
        )}
        {step.assigneeType === 'User' && (
          <div className="wfb-field">
            <span className="wfb-field__label">Người thực hiện</span>
            <Select
              aria-label="Người thực hiện"
              placeholder="Chọn người"
              showSearch
              optionFilterProp="label"
              value={step.userId ?? undefined}
              options={users}
              onChange={(v) => onChange({ userId: v })}
            />
          </div>
        )}
        <label className="wfb-field">
          <span className="wfb-field__label">Thời hạn</span>
          <InputNumber
            style={{ width: '100%' }}
            min={1}
            addonAfter="giờ"
            placeholder="SLA"
            value={step.slaHours ?? undefined}
            onChange={(v) => onChange({ slaHours: v ?? null })}
          />
        </label>
        <div className="wfb-field wfb-field--wide">
          <span className="wfb-field__label">Được trả về bước</span>
          <Select
            mode="multiple"
            aria-label="Được trả về bước"
            placeholder="Không cho trả lại"
            value={step.returnTo}
            options={[
              { value: CREATOR, label: 'Người lập (bản nháp)' },
              ...previous.map((p, i) => ({ value: p.key, label: `${i + 1}. ${p.name || p.code}` })),
            ]}
            onChange={(v) => onChange({ returnTo: v })}
          />
        </div>
        <label className="wfb-field">
          <span className="wfb-field__label">
            Neo chữ ký{' '}
            <Tooltip title="Tên thẻ {{SIG_...}} trong mẫu tài liệu; để trống = theo mã bước">
              <Info size={12} aria-hidden />
            </Tooltip>
          </span>
          <Input
            className="mono"
            placeholder={`SIG_${step.code}`}
            value={step.signatureAnchor ?? undefined}
            onChange={(e) => onChange({ signatureAnchor: e.target.value || null })}
          />
        </label>
        <div className="wfb-field wfb-field--switches">
          <label className="wfb-switch">
            <Switch size="small" checked={!step.isOptional} onChange={(v) => onChange({ isOptional: !v })} />
            {step.isOptional ? 'Tùy chọn' : 'Bắt buộc'}
          </label>
          <label className="wfb-switch">
            <Switch size="small" checked={step.allowEdit} onChange={(v) => onChange({ allowEdit: v })} />
            Được sửa tài liệu
          </label>
        </div>
      </div>
      <div className="wfb-step__tools">
        <Tooltip title="Lên trên">
          <button
            type="button"
            className="wfb-step__tool"
            aria-label={`Đưa bước ${index + 1} lên trên`}
            disabled={!onUp}
            onClick={onUp}
          >
            <ArrowUp size={15} aria-hidden />
          </button>
        </Tooltip>
        <Tooltip title="Xóa bước">
          <button
            type="button"
            className="wfb-step__tool wfb-step__tool--danger"
            aria-label={`Xóa bước ${index + 1}`}
            onClick={onRemove}
          >
            <Trash2 size={15} aria-hidden />
          </button>
        </Tooltip>
      </div>
    </div>
  );
}

/** Cấu hình luồng ký (spec §5.7): ordered steps per document type, drag to reorder. */
export function WorkflowTemplatesPage() {
  const templates = useWorkflowTemplates();
  const save = useSaveWorkflowTemplate();
  const roles = useLookup('roles');
  const users = useLookup('internal-users');
  const onError = useApiError();
  const { message } = App.useApp();
  const [draft, setDraft] = useState<Draft | null>(null);
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  const roleOptions = useMemo(
    () => (roles.data ?? []).map((r) => ({ value: r.code, label: r.name })),
    [roles.data],
  );
  const userOptions = useMemo(
    () => (users.data ?? []).map((u) => ({ value: u.id, label: u.name })),
    [users.data],
  );

  const updateStep = (key: string, patch: Partial<StepDraft>) =>
    setDraft((d) => d && { ...d, steps: d.steps.map((s) => (s.key === key ? { ...s, ...patch } : s)) });

  const move = (from: number, to: number) =>
    setDraft((d) => {
      if (!d) return d;
      const steps = arrayMove(d.steps, from, to);
      // A step can only return to an earlier step: drop targets that are now after it.
      return {
        ...d,
        steps: steps.map((s, i) => ({
          ...s,
          returnTo: s.returnTo.filter((k) => k === CREATOR || steps.slice(0, i).some((p) => p.key === k)),
        })),
      };
    });

  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (!draft || !over || active.id === over.id) return;
    move(
      draft.steps.findIndex((s) => s.key === active.id),
      draft.steps.findIndex((s) => s.key === over.id),
    );
  };

  const submit = async () => {
    if (!draft) return;
    const orderOf = (key: string) => (key === CREATOR ? 0 : draft.steps.findIndex((s) => s.key === key) + 1);
    try {
      await save.mutateAsync({
        id: draft.id,
        body: {
          code: draft.code,
          name: draft.name,
          documentType: draft.documentType,
          condition: draft.condition ?? null,
          isDefault: draft.isDefault,
          isActive: draft.isActive,
          steps: draft.steps.map((s) => ({
            code: s.code,
            name: s.name,
            actionType: s.actionType,
            assigneeType: s.assigneeType,
            roleCode: s.roleCode ?? null,
            userId: s.userId ?? null,
            allowEdit: s.allowEdit,
            isOptional: s.isOptional,
            returnTo: s.returnTo.map(orderOf).sort((a, b) => a - b),
            signatureAnchor: s.signatureAnchor ?? null,
            slaHours: s.slaHours ?? null,
          })),
        },
      });
      message.success('Đã lưu luồng ký');
      setDraft(null);
    } catch (e) {
      onError(e);
    }
  };

  if (draft) {
    const addStep = () =>
      setDraft({
        ...draft,
        steps: [
          ...draft.steps,
          {
            key: newKey(),
            code: `STEP${draft.steps.length + 1}`,
            name: '',
            actionType: 'Sign',
            assigneeType: 'Role',
            allowEdit: false,
            isOptional: false,
            returnTo: [CREATOR],
            slaHours: 24,
          },
        ],
      });
    return (
      <div className="wfb-builder">
        <DocHeader
          back={{ to: '/quan-tri/luong-ky', label: 'Cấu hình luồng ký', onClick: () => setDraft(null) }}
          plain
          code={draft.id ? 'Sửa luồng ký' : 'Thêm luồng ký'}
          sub={
            <>
              {draft.code && <span className="mono">{draft.code}</span>}
              {draft.code && ' · '}
              {draft.name || 'Chưa đặt tên'} · {draft.steps.length} bước · áp dụng cho tài liệu gửi ký sau khi
              lưu
            </>
          }
          actions={
            <>
              <Button onClick={() => setDraft(null)}>Hủy</Button>
              <Button
                type="primary"
                icon={<Save size={16} aria-hidden />}
                loading={save.isPending}
                onClick={submit}
              >
                Lưu luồng
              </Button>
            </>
          }
        />
        <section className="wfb-config">
          <label className="wfb-field">
            <span className="wfb-field__label">Mã luồng</span>
            <Input
              className="mono"
              value={draft.code}
              disabled={!!draft.id}
              onChange={(e) => setDraft({ ...draft, code: e.target.value.toUpperCase() })}
            />
          </label>
          <label className="wfb-field">
            <span className="wfb-field__label">Tên luồng</span>
            <Input value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
          </label>
          <div className="wfb-field">
            <span className="wfb-field__label">Loại tài liệu</span>
            <Select
              aria-label="Loại tài liệu"
              value={draft.documentType}
              disabled={!!draft.id}
              options={statusOptions('documentType')}
              onChange={(v) => setDraft({ ...draft, documentType: v as DocumentType })}
            />
          </div>
          <label className="wfb-field">
            <span className="wfb-field__label">
              Điều kiện áp dụng (tùy chọn){' '}
              <Tooltip title="Khóa=giá trị; nhiều giá trị cách nhau dấu phẩy. Khóa: Domain (Drug, MedicalSupply, Chemical, Equipment, Administration), Facility (mã cơ sở), ProcurementType (Bidding, NonBidding), ContractKind (UnitPriceCallOff, LumpSum), RequiresWarehouseReceipt (true, false). Luồng khớp nhiều điều kiện nhất được dùng; không khớp thì dùng luồng mặc định.">
                <Info size={12} aria-hidden />
              </Tooltip>
            </span>
            <Input
              className="mono"
              placeholder="VD: ProcurementType=NonBidding; Domain=Drug"
              value={draft.condition ?? undefined}
              onChange={(e) => setDraft({ ...draft, condition: e.target.value || null })}
            />
          </label>
          <div className="wfb-field wfb-field--switches">
            <label className="wfb-switch">
              <Switch
                size="small"
                checked={draft.isDefault}
                onChange={(v) => setDraft({ ...draft, isDefault: v })}
              />
              Mặc định
            </label>
            <label className="wfb-switch">
              <Switch
                size="small"
                checked={draft.isActive}
                onChange={(v) => setDraft({ ...draft, isActive: v })}
              />
              Hoạt động
            </label>
          </div>
        </section>
        <Banner
          tone="info"
          title="Thay đổi chỉ áp dụng cho các luồng gửi ký sau khi lưu; luồng đang chạy giữ nguyên các bước cũ."
        />
        <div className="wfb-steps">
          <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}>
            <SortableContext items={draft.steps.map((s) => s.key)} strategy={verticalListSortingStrategy}>
              {draft.steps.map((s, i) => (
                <SortableStep
                  key={s.key}
                  step={s}
                  index={i}
                  previous={draft.steps.slice(0, i)}
                  roles={roleOptions}
                  users={userOptions}
                  onChange={(patch) => updateStep(s.key, patch)}
                  onUp={i > 0 ? () => move(i, i - 1) : undefined}
                  onRemove={() =>
                    setDraft({
                      ...draft,
                      steps: draft.steps
                        .filter((x) => x.key !== s.key)
                        .map((x) => ({ ...x, returnTo: x.returnTo.filter((k) => k !== s.key) })),
                    })
                  }
                />
              ))}
            </SortableContext>
          </DndContext>
          <button type="button" className="wfb-add" onClick={addStep}>
            <Plus size={16} aria-hidden />
            Thêm bước ({statusLabel('workflowAction', 'Sign')}, duyệt…)
          </button>
        </div>
        <section className="wfb-preview" aria-label="Xem trước luồng">
          <span className="wfb-preview__label">Xem trước:</span>
          {draft.steps.map((s, i) => (
            <span key={s.key} className="wfb-preview__item">
              <span className={`wfb-preview__chip${s.isOptional ? ' is-optional' : ''}`}>
                {i + 1}. {s.name || s.code}
              </span>
              <ChevronRight size={13} aria-hidden />
            </span>
          ))}
          <span className="wfb-preview__done">Hoàn tất</span>
        </section>
      </div>
    );
  }

  return (
    <>
      <PageHeader
        title="Cấu hình luồng ký"
        subtitle="Các bước xử lý, ký số theo từng loại tài liệu"
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setDraft(toDraft())}>
            Thêm luồng
          </Button>
        }
      />
      <Panel title={`Luồng ký (${templates.data?.length ?? 0})`} flush>
        <Table<WorkflowTemplateDto>
          rowKey="id"
          size="middle"
          loading={templates.isLoading}
          dataSource={templates.data ?? []}
          pagination={false}
          scroll={{ x: 'max-content' }}
          onRow={(t) => ({ onClick: () => setDraft(toDraft(t)), style: { cursor: 'pointer' } })}
          columns={[
            { title: 'Mã', dataIndex: 'code', render: (v: string) => <span className="mono">{v}</span> },
            { title: 'Tên luồng', dataIndex: 'name' },
            {
              title: 'Loại tài liệu',
              dataIndex: 'documentType',
              render: (v: string) => <StatusTag domain="documentType" value={v} />,
            },
            {
              title: 'Các bước',
              key: 'steps',
              render: (_, t) => (
                <span className="wfb-preview wfb-preview--inline">
                  {t.steps.map((s, i) => (
                    <span key={s.code} className="wfb-preview__item">
                      <span className={`wfb-preview__chip${s.isOptional ? ' is-optional' : ''}`}>
                        {i + 1}. {s.name}
                      </span>
                      {i < t.steps.length - 1 && <ChevronRight size={13} aria-hidden />}
                    </span>
                  ))}
                </span>
              ),
            },
            {
              title: 'Trạng thái',
              key: 'status',
              render: (_, t) => (
                <Space size={4} wrap>
                  {t.isDefault && <Tag color="blue">Mặc định</Tag>}
                  <StatusTag domain="active" value={t.isActive} />
                  {t.runningCount > 0 && <Tag color="processing">{t.runningCount} đang chạy</Tag>}
                </Space>
              ),
            },
            {
              title: '',
              key: 'actions',
              render: (_, t) => (
                <Button size="small" icon={<EditOutlined />} onClick={() => setDraft(toDraft(t))}>
                  Sửa
                </Button>
              ),
            },
          ]}
        />
      </Panel>
    </>
  );
}
