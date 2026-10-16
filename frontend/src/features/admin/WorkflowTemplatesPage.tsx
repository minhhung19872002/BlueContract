import {
  Alert,
  App,
  Button,
  Card,
  Col,
  Drawer,
  Form,
  Input,
  InputNumber,
  Row,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import { DeleteOutlined, EditOutlined, HolderOutlined, PlusOutlined } from '@ant-design/icons';
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
}: {
  step: StepDraft;
  index: number;
  previous: StepDraft[];
  roles: { value: string; label: string }[];
  users: { value: string; label: string }[];
  onChange: (patch: Partial<StepDraft>) => void;
  onRemove: () => void;
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: step.key,
  });
  return (
    <Card
      ref={setNodeRef}
      size="small"
      style={{
        transform: CSS.Transform.toString(transform),
        transition,
        opacity: isDragging ? 0.6 : 1,
        marginBottom: 8,
      }}
      title={
        <Space>
          <Button
            type="text"
            size="small"
            icon={<HolderOutlined />}
            {...attributes}
            {...listeners}
            aria-label="Kéo để đổi thứ tự"
            style={{ cursor: 'grab' }}
          />
          <Typography.Text strong>Bước {index + 1}</Typography.Text>
          <StatusTag domain="workflowAction" value={step.actionType} />
        </Space>
      }
      extra={
        <Button
          type="text"
          danger
          size="small"
          icon={<DeleteOutlined />}
          onClick={onRemove}
          aria-label="Xóa bước"
        />
      }
    >
      <Row gutter={[8, 8]}>
        <Col xs={24} md={8}>
          <Input
            addonBefore="Mã"
            value={step.code}
            onChange={(e) => onChange({ code: e.target.value.toUpperCase() })}
          />
        </Col>
        <Col xs={24} md={16}>
          <Input
            addonBefore="Tên bước"
            value={step.name}
            onChange={(e) => onChange({ name: e.target.value })}
          />
        </Col>
        <Col xs={24} md={8}>
          <Select
            style={{ width: '100%' }}
            value={step.actionType}
            options={statusOptions('workflowAction')}
            onChange={(v) => onChange({ actionType: v as WorkflowActionType })}
          />
        </Col>
        <Col xs={24} md={8}>
          <Select
            style={{ width: '100%' }}
            value={step.assigneeType}
            options={statusOptions('assigneeType')}
            onChange={(v) => onChange({ assigneeType: v as AssigneeType, roleCode: null, userId: null })}
          />
        </Col>
        <Col xs={24} md={8}>
          {step.assigneeType === 'Role' && (
            <Select
              style={{ width: '100%' }}
              placeholder="Vai trò"
              showSearch
              optionFilterProp="label"
              value={step.roleCode ?? undefined}
              options={roles}
              onChange={(v) => onChange({ roleCode: v })}
            />
          )}
          {step.assigneeType === 'User' && (
            <Select
              style={{ width: '100%' }}
              placeholder="Người thực hiện"
              showSearch
              optionFilterProp="label"
              value={step.userId ?? undefined}
              options={users}
              onChange={(v) => onChange({ userId: v })}
            />
          )}
        </Col>
        <Col xs={24} md={12}>
          <Select
            mode="multiple"
            style={{ width: '100%' }}
            placeholder="Được trả về bước"
            value={step.returnTo}
            options={[
              { value: CREATOR, label: 'Người lập (bản nháp)' },
              ...previous.map((p, i) => ({ value: p.key, label: `${i + 1}. ${p.name || p.code}` })),
            ]}
            onChange={(v) => onChange({ returnTo: v })}
          />
        </Col>
        <Col xs={12} md={6}>
          <InputNumber
            style={{ width: '100%' }}
            min={1}
            addonAfter="giờ"
            placeholder="SLA"
            value={step.slaHours ?? undefined}
            onChange={(v) => onChange({ slaHours: v ?? null })}
          />
        </Col>
        <Col xs={12} md={6}>
          <Tooltip title="Vị trí đặt chữ ký: tên thẻ {{SIG_...}} trong mẫu; để trống = theo mã bước">
            <Input
              placeholder="Neo chữ ký"
              value={step.signatureAnchor ?? undefined}
              onChange={(e) => onChange({ signatureAnchor: e.target.value || null })}
            />
          </Tooltip>
        </Col>
        <Col span={24}>
          <Space wrap>
            <Switch checked={step.allowEdit} onChange={(v) => onChange({ allowEdit: v })} /> Được sửa tài liệu
            <Switch checked={step.isOptional} onChange={(v) => onChange({ isOptional: v })} /> Bước tùy chọn
          </Space>
        </Col>
      </Row>
    </Card>
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

  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over || active.id === over.id) return;
    setDraft((d) => {
      if (!d) return d;
      const from = d.steps.findIndex((s) => s.key === active.id);
      const to = d.steps.findIndex((s) => s.key === over.id);
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
      <Card>
        <Table<WorkflowTemplateDto>
          rowKey="id"
          size="middle"
          loading={templates.isLoading}
          dataSource={templates.data ?? []}
          pagination={false}
          scroll={{ x: 'max-content' }}
          columns={[
            { title: 'Mã', dataIndex: 'code' },
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
                <Typography.Text type="secondary">{t.steps.map((s) => s.name).join(' → ')}</Typography.Text>
              ),
            },
            {
              title: 'Trạng thái',
              key: 'status',
              render: (_, t) => (
                <Space size={4}>
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
      </Card>

      <Drawer
        open={!!draft}
        onClose={() => setDraft(null)}
        width="min(960px, 100vw)"
        title={draft?.id ? `Sửa luồng ký — ${draft.code}` : 'Thêm luồng ký'}
        destroyOnHidden
        extra={
          <Button type="primary" loading={save.isPending} onClick={submit}>
            Lưu
          </Button>
        }
      >
        {draft && (
          <>
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 12 }}
              message="Thay đổi chỉ áp dụng cho các luồng gửi ký sau khi lưu; luồng đang chạy giữ nguyên các bước cũ."
            />
            <Form layout="vertical">
              <Row gutter={12}>
                <Col xs={24} md={8}>
                  <Form.Item label="Mã luồng" required>
                    <Input
                      value={draft.code}
                      disabled={!!draft.id}
                      onChange={(e) => setDraft({ ...draft, code: e.target.value.toUpperCase() })}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={16}>
                  <Form.Item label="Tên luồng" required>
                    <Input
                      value={draft.name}
                      onChange={(e) => setDraft({ ...draft, name: e.target.value })}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={8}>
                  <Form.Item label="Loại tài liệu" required>
                    <Select
                      value={draft.documentType}
                      disabled={!!draft.id}
                      options={statusOptions('documentType')}
                      onChange={(v) => setDraft({ ...draft, documentType: v as DocumentType })}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={8}>
                  <Form.Item label="Điều kiện áp dụng (tùy chọn)">
                    <Input
                      placeholder="VD: Domain=THUOC"
                      value={draft.condition ?? undefined}
                      onChange={(e) => setDraft({ ...draft, condition: e.target.value || null })}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={8}>
                  <Form.Item label=" ">
                    <Space wrap>
                      <Switch
                        checked={draft.isDefault}
                        onChange={(v) => setDraft({ ...draft, isDefault: v })}
                      />{' '}
                      Mặc định
                      <Switch
                        checked={draft.isActive}
                        onChange={(v) => setDraft({ ...draft, isActive: v })}
                      />{' '}
                      Hoạt động
                    </Space>
                  </Form.Item>
                </Col>
              </Row>
            </Form>

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
            <Button
              block
              type="dashed"
              icon={<PlusOutlined />}
              onClick={() =>
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
                })
              }
            >
              Thêm bước ({statusLabel('workflowAction', 'Sign')}, duyệt...)
            </Button>
          </>
        )}
      </Drawer>
    </>
  );
}
