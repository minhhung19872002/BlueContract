import { App, Form, Input, Modal, Radio, Space } from 'antd';
import { Undo2 } from 'lucide-react';
import { useReturnStep, type WorkflowDto } from '../../api/signing';
import { useApiError } from '../../components/common';

/** "Trả lại" dialog (design "Modal xác nhận nguy hiểm"): target step + mandatory reason. Shared by every signing screen. */
export function ReturnDialog({
  wf,
  open,
  onClose,
  onDone,
}: {
  wf: WorkflowDto;
  open: boolean;
  onClose: () => void;
  onDone?: () => void;
}) {
  const [form] = Form.useForm<{ toOrder: number; reason: string }>();
  const returnStep = useReturnStep();
  const onError = useApiError();
  const { message } = App.useApp();
  return (
    <Modal
      title={
        <span className="danger-modal__title">
          <span className="danger-modal__icon" aria-hidden>
            <Undo2 size={20} strokeWidth={1.75} />
          </span>
          Trả lại tài liệu?
        </span>
      }
      open={open}
      onCancel={onClose}
      okText="Trả lại"
      cancelText="Hủy"
      okButtonProps={{ danger: true, loading: returnStep.isPending }}
      onOk={async () => {
        const v = await form.validateFields();
        try {
          await returnStep.mutateAsync({ stepId: wf.myStepId!, toOrder: v.toOrder, reason: v.reason });
          message.success('Đã trả lại tài liệu');
          form.resetFields();
          onClose();
          onDone?.();
        } catch (e) {
          onError(e);
        }
      }}
      destroyOnHidden
    >
      <p className="danger-modal__body">
        Tài liệu sẽ quay về bước được chọn; luồng ký chuyển sang “Bị trả lại”.
      </p>
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
          label="Lý do"
          rules={[{ required: true, whitespace: true, message: 'Vui lòng nhập lý do' }]}
        >
          <Input.TextArea rows={3} maxLength={2000} showCount placeholder="Nhập lý do…" />
        </Form.Item>
      </Form>
    </Modal>
  );
}
