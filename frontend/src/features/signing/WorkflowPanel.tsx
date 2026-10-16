import { Alert, App, Button, Empty, Form, Input, Modal, Tooltip } from 'antd';
import {
  Ban,
  Check,
  Clock,
  FileBadge,
  MessageSquare,
  PenLine,
  RefreshCw,
  Signature,
  Stamp,
  Undo2,
} from 'lucide-react';
import { useState } from 'react';
import {
  useApproveStep,
  useCancelWorkflow,
  useCommentWorkflow,
  useResubmitWorkflow,
  type WorkflowDto,
  type WorkflowStepDto,
} from '../../api/signing';
import { StatusTag, statusLabel, statusTone, type Tone } from '../../components/StatusTag';
import { useApiError } from '../../components/common';
import { formatDateTime } from '../../utils/format';
import { ReturnDialog } from './ReturnDialog';
import { SignButton } from './SignButton';
import './workflowPanel.css';

const initials = (name?: string | null) => {
  const parts = (name ?? 'Hệ thống').trim().split(/\s+/).filter(Boolean);
  return ((parts.length > 1 ? (parts.at(-2)?.[0] ?? '') : '') + (parts.at(-1)?.[0] ?? '')).toUpperCase();
};

const EVENT_TONE: Record<string, Tone> = {
  Started: 'primary',
  Resubmitted: 'primary',
  Approved: 'success',
  Completed: 'success',
  SignatureAdded: 'purple',
  Returned: 'danger',
  Commented: 'info',
  Restarted: 'orange',
};

function TextDialog({
  title,
  label,
  okText,
  open,
  loading,
  required = true,
  danger,
  onClose,
  onSubmit,
}: {
  title: string;
  label: string;
  okText: string;
  open: boolean;
  loading: boolean;
  required?: boolean;
  danger?: boolean;
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
      cancelText="Hủy"
      okButtonProps={{ loading, danger }}
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

/**
 * Actions of the current user on the workflow (Ký số / Đồng ý / Trả lại / Ý kiến) and of the sender (Trình lại / Hủy luồng).
 * `part` limits the buttons to the step ("mine") or to the sender's footer ("sender"); omitted = everything.
 */
export function WorkflowActions({
  wf,
  onDone,
  part,
}: {
  wf: WorkflowDto;
  onDone?: () => void;
  part?: 'mine' | 'sender';
}) {
  const [dialog, setDialog] = useState<'return' | 'comment' | 'approve' | 'cancel' | null>(null);
  const approve = useApproveStep();
  const comment = useCommentWorkflow();
  const resubmit = useResubmitWorkflow();
  const cancel = useCancelWorkflow();
  const onError = useApiError();
  const { message } = App.useApp();
  const myStep = wf.steps.find((s) => s.id === wf.myStepId);
  const close = () => setDialog(null);
  // Business precondition of my step (e.g. receipt validation): blocked, or a reason is required to sign/approve.
  const gate = myStep ? wf.gate : null;
  const [reason, setReason] = useState('');
  const blocked = !!gate?.blocked;
  const reasonMissing = !!gate?.reasonRequired && !reason.trim();
  const showMine = part !== 'sender';
  const showSender = part !== 'mine';

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

  const signLabel = !myStep
    ? ''
    : wf.myCaption
      ? `Ký (${wf.myCaption})`
      : myStep.actionType === 'Initial'
        ? 'Ký nháy'
        : statusLabel('workflowAction', myStep.actionType);
  const reviewLike = myStep && (myStep.actionType === 'Review' || myStep.actionType === 'SupplierReview');

  return (
    <div className="wf-actions">
      {showMine && gate && (
        <Alert type={gate.blocked ? 'error' : 'warning'} showIcon message={gate.message} />
      )}
      {showMine && gate?.reasonRequired && !gate.blocked && (
        <Input.TextArea
          rows={2}
          maxLength={2000}
          status={reasonMissing ? 'warning' : undefined}
          placeholder="Lý do chấp nhận cảnh báo (bắt buộc để ký) — được lưu vào ý kiến của bước"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
      )}
      <div className="wf-actions__buttons">
        {showMine &&
          myStep &&
          (myStep.requiresSignature ? (
            blocked || reasonMissing ? (
              <Tooltip title={blocked ? gate?.message : 'Nhập lý do chấp nhận cảnh báo trước khi ký'}>
                <Button type="primary" size="small" icon={<PenLine size={14} aria-hidden />} disabled>
                  {signLabel}
                </Button>
              </Tooltip>
            ) : (
              <SignButton
                stepId={myStep.id}
                label={signLabel}
                comment={reason.trim() || undefined}
                onSigned={onDone}
              />
            )
          ) : (
            <Button
              type="primary"
              size="small"
              icon={<Check size={14} aria-hidden />}
              disabled={blocked}
              onClick={() => setDialog('approve')}
            >
              {reviewLike ? 'Đồng ý' : 'Duyệt'}
            </Button>
          ))}
        {showMine && myStep && wf.returnTargets.length > 0 && (
          <Button
            size="small"
            danger
            icon={<Undo2 size={14} aria-hidden />}
            onClick={() => setDialog('return')}
          >
            Trả lại
          </Button>
        )}
        {showMine && (
          <Button
            size="small"
            type="text"
            icon={<MessageSquare size={14} aria-hidden />}
            onClick={() => setDialog('comment')}
          >
            Ý kiến
          </Button>
        )}
        {showSender && wf.canResubmit && (
          <Button
            size="small"
            icon={<RefreshCw size={14} aria-hidden />}
            loading={resubmit.isPending}
            onClick={() => guarded(() => resubmit.mutateAsync(wf.instanceId), 'Đã trình ký lại')}
          >
            Trình lại
          </Button>
        )}
        {showSender && wf.canCancel && (
          <Button
            size="small"
            danger
            icon={<Ban size={14} aria-hidden />}
            onClick={() => setDialog('cancel')}
          >
            Hủy luồng
          </Button>
        )}
      </div>

      <ReturnDialog wf={wf} open={dialog === 'return'} onClose={close} onDone={onDone} />
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
        label={gate?.reasonRequired ? 'Lý do chấp nhận cảnh báo' : 'Ý kiến (không bắt buộc)'}
        okText="Xác nhận"
        required={!!gate?.reasonRequired}
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
        title="Hủy luồng ký?"
        label="Lý do"
        okText="Hủy luồng"
        danger
        open={dialog === 'cancel'}
        loading={cancel.isPending}
        onClose={close}
        onSubmit={(text) =>
          guarded(() => cancel.mutateAsync({ instanceId: wf.instanceId, reason: text }), 'Đã hủy luồng ký')
        }
      />
    </div>
  );
}

function StepItem({
  s,
  last,
  wf,
  onDone,
}: {
  s: WorkflowStepDto;
  last: boolean;
  wf: WorkflowDto;
  onDone?: () => void;
}) {
  const done = s.status === 'Done';
  const active = s.status === 'Active';
  const skipped = s.status === 'Skipped';
  const mine = active && s.id === wf.myStepId && (wf.status === 'Running' || wf.status === 'Returned');
  return (
    <li className={`wf-step${done ? ' wf-step--done' : ''}${active ? ' wf-step--active' : ''}`}>
      <div className="wf-step__rail">
        <span className="wf-step__dot">
          {done ? <Check size={14} strokeWidth={2.5} aria-hidden /> : s.order}
        </span>
        {!last && <span className="wf-step__line" />}
      </div>
      <div className="wf-step__body">
        <div className="wf-step__head">
          <span className="wf-step__title">{s.name}</span>
          <StatusTag domain="workflowAction" value={s.actionType} />
          {s.isOptional && <span className="wf-step__optional">Tùy chọn</span>}
        </div>
        <div className="wf-step__assignee">{s.assigneeDisplay}</div>
        {done && s.actedByName && (
          <div className="wf-step__signer">
            {s.signatureId && (
              <Tooltip title="Đã gắn chữ ký số vào PDF">
                <FileBadge
                  size={14}
                  strokeWidth={1.75}
                  className="wf-step__badge"
                  aria-label="Đã gắn chữ ký số"
                />
              </Tooltip>
            )}
            <span>
              <strong>{s.actedByName}</strong> · {formatDateTime(s.actedAt)}
            </span>
          </div>
        )}
        {skipped && <div className="wf-step__deadline">Bỏ qua</div>}
        {active && s.dueAt && (
          <div className={`wf-step__deadline${s.isOverdue ? ' wf-step__deadline--overdue' : ''}`}>
            <Clock size={14} strokeWidth={1.75} aria-hidden />
            Hạn xử lý {formatDateTime(s.dueAt)}
            {s.isOverdue && <StatusTag domain="flag" value="Overdue" />}
          </div>
        )}
        {s.comment && <div className="wf-step__comment">“{s.comment}”</div>}
        {mine && <WorkflowActions wf={wf} onDone={onDone} part="mine" />}
      </div>
    </li>
  );
}

/** Steps of the workflow (design "Luồng ký"): number / check, action tag, assignee, signer and time, SLA. */
export function WorkflowSteps({ wf, onDone }: { wf: WorkflowDto; onDone?: () => void }) {
  return (
    <ol className="wf-steps">
      {wf.steps.map((s, i) => (
        <StepItem key={s.id} s={s} wf={wf} last={i === wf.steps.length - 1} onDone={onDone} />
      ))}
    </ol>
  );
}

/** Ý kiến / sự kiện của luồng (trả lại, ý kiến chuyển tiếp, chữ ký...) as an avatar timeline. */
export function WorkflowHistory({ wf }: { wf: WorkflowDto }) {
  if (wf.events.length === 0)
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có ý kiến" />;
  const events = [...wf.events].reverse();
  return (
    <div className="wf-timeline">
      {events.map((e, i) => {
        const tone = EVENT_TONE[e.type] ?? 'neutral';
        return (
          <div key={i} className="wf-timeline__item">
            <div className="wf-timeline__rail">
              <span
                className="wf-timeline__avatar"
                style={{ background: `var(--bp-${tone}-bg)`, color: `var(--bp-${tone}-fg)` }}
              >
                {initials(e.actorName)}
              </span>
              {i < events.length - 1 && <span className="wf-timeline__line" />}
            </div>
            <div className="wf-timeline__body">
              <div className="wf-timeline__head">
                <span className="wf-timeline__who">{e.actorName ?? 'Hệ thống'}</span>
                <StatusTag domain="workflowEvent" value={e.type} />
                {e.stepName && <span className="wf-timeline__step">· {e.stepName}</span>}
              </div>
              <div className="wf-timeline__at">{formatDateTime(e.at)}</div>
              {e.type === 'Returned' && e.targetOrder !== null && e.targetOrder !== undefined && (
                <div className="wf-timeline__at">
                  Trả về: {e.targetOrder === 0 ? 'người lập' : `bước ${e.targetOrder}`}
                </div>
              )}
              {e.note && <div className="wf-timeline__note">{e.note}</div>}
            </div>
          </div>
        );
      })}
    </div>
  );
}

/** Signatures embedded in the PDF (design "Chữ ký số (n)"). */
export function WorkflowSignatures({ wf }: { wf: WorkflowDto }) {
  return (
    <div className="wf-signatures">
      {wf.signatures.map((s) => {
        const tone = statusTone('signatureKind', s.kind);
        const Icon = s.kind === 'Organization' ? Stamp : s.kind === 'Initial' ? Signature : PenLine;
        return (
          <div key={s.id} className="wf-signature">
            <span
              className="wf-signature__icon"
              style={{ background: `var(--bp-${tone}-bg)`, color: `var(--bp-${tone}-fg)` }}
              aria-hidden
            >
              <Icon size={15} strokeWidth={1.75} />
            </span>
            <div className="wf-signature__body">
              <div className="wf-signature__head">
                <span className="wf-signature__name">{s.signerName}</span>
                <StatusTag domain="signatureKind" value={s.kind} />
                {s.caption && <span className="wf-signature__caption">{s.caption}</span>}
              </div>
              {s.signerTitle && <div className="wf-signature__org">{s.signerTitle}</div>}
              <div className="wf-signature__meta">
                {s.provider} · {formatDateTime(s.signedAt)}
              </div>
              <div className="wf-signature__meta mono">Số serial {s.certificateSerial}</div>
            </div>
          </div>
        );
      })}
    </div>
  );
}

/** Complete right column of a document (design/Page Detail.dc.html): Luồng ký, Ý kiến & lịch sử, Chữ ký số. */
export function WorkflowCard({ wf, onDone }: { wf: WorkflowDto; onDone?: () => void }) {
  const doneCount = wf.steps.filter((s) => s.status === 'Done').length;
  const running = wf.status === 'Running' || wf.status === 'Returned';
  const senderActions = running && (wf.canResubmit || wf.canCancel);
  return (
    <div className="wf-column">
      <section className="wf-card sign-flow">
        <div className="wf-card__head">
          <span className="wf-card__title">Luồng ký</span>
          <StatusTag
            domain="workflowStatus"
            value={wf.status}
            suffix={running ? ` · ${doneCount}/${wf.steps.length}` : undefined}
          />
        </div>
        <div className="wf-card__sub">
          {wf.startedByName} gửi lúc {formatDateTime(wf.startedAt)}
        </div>
        <WorkflowSteps wf={wf} onDone={onDone} />
        {running && !wf.myStepId && (
          <div className="wf-card__comment-only">
            <WorkflowActions wf={wf} onDone={onDone} part="mine" />
          </div>
        )}
        {senderActions && (
          <div className="wf-card__footer">
            <span className="wf-card__footer-label">Người gửi</span>
            <WorkflowActions wf={wf} onDone={onDone} part="sender" />
          </div>
        )}
      </section>
      <section className="wf-card">
        <div className="wf-card__head">
          <span className="wf-card__title">Ý kiến &amp; lịch sử</span>
        </div>
        <div className="wf-card__body">
          <WorkflowHistory wf={wf} />
        </div>
      </section>
      {wf.signatures.length > 0 && (
        <section className="wf-card">
          <div className="wf-card__head">
            <span className="wf-card__title">Chữ ký số ({wf.signatures.length})</span>
          </div>
          <WorkflowSignatures wf={wf} />
        </section>
      )}
    </div>
  );
}
