import {
  App,
  Button,
  Card,
  Empty,
  Form,
  Input,
  Modal,
  Radio,
  Space,
  Steps,
  Tag,
  Timeline,
  Tooltip,
  Typography,
} from 'antd';
import {
  CheckOutlined,
  ClockCircleOutlined,
  CommentOutlined,
  RollbackOutlined,
  SafetyCertificateOutlined,
  StopOutlined,
  SyncOutlined,
} from '@ant-design/icons';
import { useState } from 'react';
import {
  useApproveStep,
  useCancelWorkflow,
  useCommentWorkflow,
  useResubmitWorkflow,
  useReturnStep,
  type WorkflowDto,
  type WorkflowStepDto,
} from '../../api/signing';
import { StatusTag, statusLabel } from '../../components/StatusTag';
import { useApiError } from '../../components/common';
import { formatDateTime } from '../../utils/format';
import { SignButton } from './SignButton';

function stepStatus(s: WorkflowStepDto): 'wait' | 'process' | 'finish' | 'error' {
  if (s.status === 'Done' || s.status === 'Skipped') return 'finish';
  if (s.status === 'Active') return s.isOverdue ? 'error' : 'process';
  return 'wait';
}

/** Steps of the workflow with assignee, who acted, when and the SLA (spec §6.7 "lịch sử luồng"). */
export function WorkflowSteps({ wf }: { wf: WorkflowDto }) {
  return (
    <Steps
      direction="vertical"
      size="small"
      items={wf.steps.map((s) => ({
        status: stepStatus(s),
        title: (
          <Space size={4} wrap>
            <span>
              {s.order}. {s.name}
            </span>
            <StatusTag domain="workflowAction" value={s.actionType} />
            {s.isOptional && <Tag>Tùy chọn</Tag>}
          </Space>
        ),
        description: (
          <Space direction="vertical" size={0}>
            <Typography.Text type="secondary">{s.assigneeDisplay}</Typography.Text>
            {s.status === 'Done' && (
              <Typography.Text>
                {s.actedByName} · {formatDateTime(s.actedAt)}
                {s.signatureId && (
                  <Tooltip title="Đã gắn chữ ký số vào PDF">
                    <SafetyCertificateOutlined style={{ color: '#722ed1', marginLeft: 6 }} />
                  </Tooltip>
                )}
              </Typography.Text>
            )}
            {s.status === 'Active' && s.dueAt && (
              <Typography.Text type={s.isOverdue ? 'danger' : 'secondary'}>
                <ClockCircleOutlined /> Hạn xử lý {formatDateTime(s.dueAt)}
                {s.isOverdue && ' (quá hạn)'}
              </Typography.Text>
            )}
            {s.comment && <Typography.Text italic>“{s.comment}”</Typography.Text>}
          </Space>
        ),
      }))}
    />
  );
}

/** Ý kiến / sự kiện của luồng (trả lại, ý kiến chuyển tiếp, chữ ký...). */
export function WorkflowHistory({ wf }: { wf: WorkflowDto }) {
  if (wf.events.length === 0)
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có ý kiến" />;
  return (
    <Timeline
      items={[...wf.events].reverse().map((e, i) => ({
        key: i,
        color:
          e.type === 'Returned' || e.type === 'Cancelled'
            ? 'red'
            : e.type === 'SignatureAdded'
              ? 'purple'
              : 'blue',
        children: (
          <Space direction="vertical" size={0}>
            <Space size={4} wrap>
              <StatusTag domain="workflowEvent" value={e.type} />
              <Typography.Text strong>{e.actorName ?? 'Hệ thống'}</Typography.Text>
              {e.stepName && <Typography.Text type="secondary">· {e.stepName}</Typography.Text>}
            </Space>
            {e.note && <Typography.Text style={{ whiteSpace: 'pre-line' }}>{e.note}</Typography.Text>}
            {e.type === 'Returned' && e.targetOrder !== null && e.targetOrder !== undefined && (
              <Typography.Text type="secondary">
                Trả về: {e.targetOrder === 0 ? 'người lập' : `bước ${e.targetOrder}`}
              </Typography.Text>
            )}
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {formatDateTime(e.at)}
            </Typography.Text>
          </Space>
        ),
      }))}
    />
  );
}

function ReturnDialog({ wf, open, onClose }: { wf: WorkflowDto; open: boolean; onClose: () => void }) {
  const [form] = Form.useForm<{ toOrder: number; reason: string }>();
  const returnStep = useReturnStep();
  const onError = useApiError();
  const { message } = App.useApp();
  return (
    <Modal
      title="Trả lại tài liệu"
      open={open}
      onCancel={onClose}
      okText="Trả lại"
      okButtonProps={{ danger: true, loading: returnStep.isPending }}
      onOk={async () => {
        const v = await form.validateFields();
        try {
          await returnStep.mutateAsync({ stepId: wf.myStepId!, toOrder: v.toOrder, reason: v.reason });
          message.success('Đã trả lại tài liệu');
          form.resetFields();
          onClose();
        } catch (e) {
          onError(e);
        }
      }}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" initialValues={{ toOrder: wf.returnTargets[0]?.order }}>
        <Form.Item
          name="toOrder"
          label="Trả về bước"
          rules={[{ required: true, message: 'Chọn bước trả về' }]}
        >
          <Radio.Group>
            <Space direction="vertical">
              {wf.returnTargets.map((t) => (
                <Radio key={t.order} value={t.order}>
                  {t.order === 0 ? t.name : `${t.order}. ${t.name}`}
                </Radio>
              ))}
            </Space>
          </Radio.Group>
        </Form.Item>
        <Form.Item
          name="reason"
          label="Lý do trả lại"
          rules={[{ required: true, whitespace: true, message: 'Bắt buộc nhập lý do trả lại' }]}
        >
          <Input.TextArea rows={4} maxLength={2000} showCount />
        </Form.Item>
      </Form>
    </Modal>
  );
}

function TextDialog({
  title,
  label,
  okText,
  open,
  loading,
  required = true,
  onClose,
  onSubmit,
}: {
  title: string;
  label: string;
  okText: string;
  open: boolean;
  loading: boolean;
  required?: boolean;
  onClose: () => void;
  onSubmit: (text: string) => Promise<void>;
}) {
  const [form] = Form.useForm<{ text: string }>();
  return (
    <Modal
      title={title}
      open={open}
      onCancel={onClose}
      okText={okText}
      okButtonProps={{ loading }}
      onOk={async () => {
        const v = await form.validateFields();
        await onSubmit(v.text ?? '');
        form.resetFields();
      }}
      destroyOnHidden
    >
      <Form form={form} layout="vertical">
        <Form.Item
          name="text"
          label={label}
          rules={
            required
              ? [{ required: true, whitespace: true, message: `Vui lòng nhập ${label.toLowerCase()}` }]
              : []
          }
        >
          <Input.TextArea rows={4} maxLength={2000} showCount />
        </Form.Item>
      </Form>
    </Modal>
  );
}

/** Action bar for the current user on a workflow: Ký / Đồng ý / Trả lại / Chuyển tiếp ý kiến / Trình lại / Hủy. */
export function WorkflowActions({ wf, onDone }: { wf: WorkflowDto; onDone?: () => void }) {
  const [dialog, setDialog] = useState<'return' | 'comment' | 'approve' | 'cancel' | null>(null);
  const approve = useApproveStep();
  const comment = useCommentWorkflow();
  const resubmit = useResubmitWorkflow();
  const cancel = useCancelWorkflow();
  const onError = useApiError();
  const { message } = App.useApp();
  const myStep = wf.steps.find((s) => s.id === wf.myStepId);
  const close = () => setDialog(null);

  const guarded = async (action: () => Promise<unknown>, success: string) => {
    try {
      await action();
      message.success(success);
      close();
      onDone?.();
    } catch (e) {
      onError(e);
    }
  };

  return (
    <Space wrap>
      {myStep &&
        (myStep.requiresSignature ? (
          <SignButton
            stepId={myStep.id}
            label={
              wf.myCaption
                ? `Ký (${wf.myCaption})`
                : myStep.actionType === 'Initial'
                  ? 'Ký nháy'
                  : statusLabel('workflowAction', myStep.actionType)
            }
            onSigned={onDone}
          />
        ) : (
          <Button type="primary" icon={<CheckOutlined />} onClick={() => setDialog('approve')}>
            {myStep.actionType === 'Review' || myStep.actionType === 'SupplierReview' ? 'Đồng ý' : 'Duyệt'}
          </Button>
        ))}
      {myStep && wf.returnTargets.length > 0 && (
        <Button danger icon={<RollbackOutlined />} onClick={() => setDialog('return')}>
          Trả lại
        </Button>
      )}
      <Button icon={<CommentOutlined />} onClick={() => setDialog('comment')}>
        Chuyển tiếp ý kiến
      </Button>
      {wf.canResubmit && (
        <Button
          icon={<SyncOutlined />}
          loading={resubmit.isPending}
          onClick={() => guarded(() => resubmit.mutateAsync(wf.instanceId), 'Đã trình ký lại')}
        >
          Trình ký lại
        </Button>
      )}
      {wf.canCancel && (
        <Button icon={<StopOutlined />} onClick={() => setDialog('cancel')}>
          Hủy luồng ký
        </Button>
      )}

      <ReturnDialog wf={wf} open={dialog === 'return'} onClose={close} />
      <TextDialog
        title="Chuyển tiếp ý kiến"
        label="Ý kiến"
        okText="Gửi ý kiến"
        open={dialog === 'comment'}
        loading={comment.isPending}
        onClose={close}
        onSubmit={(text) =>
          guarded(() => comment.mutateAsync({ instanceId: wf.instanceId, text }), 'Đã gửi ý kiến')
        }
      />
      <TextDialog
        title="Đồng ý / duyệt"
        label="Ý kiến (không bắt buộc)"
        okText="Xác nhận"
        required={false}
        open={dialog === 'approve'}
        loading={approve.isPending}
        onClose={close}
        onSubmit={(text) =>
          guarded(
            () => approve.mutateAsync({ stepId: myStep!.id, comment: text || undefined }),
            'Đã xác nhận',
          )
        }
      />
      <TextDialog
        title="Hủy luồng ký"
        label="Lý do hủy"
        okText="Hủy luồng"
        open={dialog === 'cancel'}
        loading={cancel.isPending}
        onClose={close}
        onSubmit={(text) =>
          guarded(() => cancel.mutateAsync({ instanceId: wf.instanceId, reason: text }), 'Đã hủy luồng ký')
        }
      />
    </Space>
  );
}

/** Complete workflow card: status, actions, steps, history and signatures. */
export function WorkflowCard({ wf, onDone }: { wf: WorkflowDto; onDone?: () => void }) {
  return (
    <Card
      title={
        <Space wrap>
          Luồng ký
          <StatusTag domain="workflowStatus" value={wf.status} />
        </Space>
      }
      extra={
        <Typography.Text type="secondary">
          {wf.startedByName} gửi lúc {formatDateTime(wf.startedAt)}
        </Typography.Text>
      }
    >
      {(wf.status === 'Running' || wf.status === 'Returned') && (
        <div style={{ marginBottom: 16 }}>
          <WorkflowActions wf={wf} onDone={onDone} />
        </div>
      )}
      <WorkflowSteps wf={wf} />
      <Typography.Title level={5}>Ý kiến & lịch sử</Typography.Title>
      <WorkflowHistory wf={wf} />
      {wf.signatures.length > 0 && (
        <>
          <Typography.Title level={5}>Chữ ký số ({wf.signatures.length})</Typography.Title>
          <Space direction="vertical" style={{ width: '100%' }}>
            {wf.signatures.map((s) => (
              <Card key={s.id} size="small">
                <Space direction="vertical" size={0}>
                  <Space wrap>
                    <SafetyCertificateOutlined style={{ color: '#722ed1' }} />
                    <Typography.Text strong>{s.signerName}</Typography.Text>
                    {s.caption && <Tag color="purple">{s.caption}</Tag>}
                    <StatusTag domain="signatureKind" value={s.kind} />
                  </Space>
                  {s.signerTitle && <Typography.Text type="secondary">{s.signerTitle}</Typography.Text>}
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {formatDateTime(s.signedAt)} · {s.provider} · Số serial {s.certificateSerial}
                  </Typography.Text>
                </Space>
              </Card>
            ))}
          </Space>
        </>
      )}
    </Card>
  );
}
