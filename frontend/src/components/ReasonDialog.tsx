import { Form, Input, Modal } from 'antd';
import type { ReactNode } from 'react';

/**
 * Confirmation that needs a reason or an opinion (design "Modal xác nhận nguy hiểm"): trả lại, hủy, gửi lại… One
 * component for every such action, so labels, validation and the danger styling stay the same everywhere. Extra form
 * fields (e.g. the step to return to) go in `children`; `onConfirm` receives the reason and their values and closes the
 * dialog when it resolves.
 */
export function ReasonDialog<V extends object = Record<string, never>>({
  open,
  title,
  icon,
  description,
  okText = 'Xác nhận',
  reasonLabel = 'Lý do',
  placeholder = 'Nhập lý do…',
  required = true,
  requiredMessage = 'Vui lòng nhập lý do',
  danger = true,
  loading,
  initialValues,
  children,
  onClose,
  onConfirm,
}: {
  open: boolean;
  title: ReactNode;
  icon?: ReactNode;
  description?: ReactNode;
  okText?: string;
  reasonLabel?: string;
  placeholder?: string;
  required?: boolean;
  requiredMessage?: string;
  danger?: boolean;
  loading?: boolean;
  initialValues?: Partial<V>;
  children?: ReactNode;
  onClose: () => void;
  onConfirm: (reason: string, values: V) => Promise<unknown>;
}) {
  const [form] = Form.useForm<V & { reason?: string }>();
  return (
    <Modal
      title={
        icon ? (
          <span className="danger-modal__title">
            <span className="danger-modal__icon" aria-hidden>
              {icon}
            </span>
            {title}
          </span>
        ) : (
          title
        )
      }
      open={open}
      onCancel={onClose}
      okText={okText}
      cancelText="Hủy"
      okButtonProps={{ danger, loading }}
      destroyOnHidden
      onOk={async () => {
        const values = await form.validateFields();
        const { reason, ...rest } = values;
        try {
          await onConfirm((reason ?? '').trim(), rest as unknown as V);
          form.resetFields();
          onClose();
        } catch {
          // the caller reported the error; keep the dialog open with what was typed
        }
      }}
    >
      {description && <p className="danger-modal__body">{description}</p>}
      <Form form={form} layout="vertical" initialValues={initialValues as never}>
        {children}
        <Form.Item
          name="reason"
          label={reasonLabel}
          rules={required ? [{ required: true, whitespace: true, message: requiredMessage }] : []}
        >
          <Input.TextArea rows={3} maxLength={2000} showCount placeholder={placeholder} autoFocus />
        </Form.Item>
      </Form>
    </Modal>
  );
}
