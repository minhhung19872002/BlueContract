import { App, Form, Radio, Space } from 'antd';
import { Undo2 } from 'lucide-react';
import { useReturnStep, type WorkflowDto } from '../../api/signing';
import { useApiError } from '../../components/common';
import { ReasonDialog } from '../../components/ReasonDialog';

/** "Trả lại" dialog: target step + mandatory reason. Shared by every signing screen. */
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
  const returnStep = useReturnStep();
  const onError = useApiError();
  const { message } = App.useApp();
  return (
    <ReasonDialog<{ toOrder: number }>
      open={open}
      title="Trả lại tài liệu?"
      icon={<Undo2 size={20} strokeWidth={1.75} />}
      description="Tài liệu sẽ quay về bước được chọn; luồng ký chuyển sang “Bị trả lại”."
      okText="Trả lại"
      loading={returnStep.isPending}
      initialValues={{ toOrder: wf.returnTargets[0]?.order }}
      onClose={onClose}
      onConfirm={async (reason, v) => {
        try {
          await returnStep.mutateAsync({ stepId: wf.myStepId!, toOrder: v.toOrder, reason });
          message.success('Đã trả lại tài liệu');
          onDone?.();
        } catch (e) {
          onError(e);
          throw e;
        }
      }}
    >
      <Form.Item name="toOrder" label="Trả về bước" rules={[{ required: true, message: 'Chọn bước trả về' }]}>
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
    </ReasonDialog>
  );
}
