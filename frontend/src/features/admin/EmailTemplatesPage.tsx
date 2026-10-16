import { App, Button, Form, Input, Modal, Popconfirm, Space, Table, Tag, Tooltip, Typography } from 'antd';
import { Eye, Pencil, RotateCcw } from 'lucide-react';
import { useRef, useState } from 'react';
import type { TextAreaRef } from 'antd/es/input/TextArea';
import {
  EMAIL_TEMPLATES as RESOURCE,
  emailTemplateUrl as url,
  previewEmailTemplate,
  useAction,
  useGet,
  type EmailPreviewDto,
  type EmailTemplateDto,
} from '../../api/admin';
import { PageHeader, useApiError } from '../../components/common';
import { formatDateTime } from '../../utils/format';
import './emailTemplates.css';

/** Mẫu email theo sự kiện (spec §3): tiêu đề + nội dung HTML có biến {{...}}, xem trước, khôi phục mặc định. */
export function EmailTemplatesPage() {
  const list = useGet<EmailTemplateDto[]>(RESOURCE, [RESOURCE]);
  const [editing, setEditing] = useState<EmailTemplateDto | null>(null);
  const { message } = App.useApp();
  const onError = useApiError();
  const reset = useAction<string>(RESOURCE, (eventType) => ({ url: url(eventType), method: 'delete' }));

  return (
    <>
      <PageHeader
        title="Mẫu email"
        subtitle="Nội dung email gửi theo từng sự kiện; giờ trong email là giờ Việt Nam"
      />
      <Table<EmailTemplateDto>
        rowKey="eventType"
        loading={list.isLoading}
        dataSource={list.data ?? []}
        pagination={false}
        scroll={{ x: 'max-content' }}
        columns={[
          {
            title: 'Sự kiện',
            dataIndex: 'name',
            render: (v: string, t) => (
              <Space direction="vertical" size={0}>
                <span>{v}</span>
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  {t.group}
                </Typography.Text>
              </Space>
            ),
          },
          {
            title: 'Tiêu đề',
            dataIndex: 'subject',
            render: (v: string) => <code className="email-tpl__code">{v}</code>,
          },
          {
            title: 'Trạng thái',
            key: 'state',
            width: 150,
            render: (_, t) =>
              t.isCustomized ? (
                <Tooltip title={t.updatedAt ? `Sửa lúc ${formatDateTime(t.updatedAt)}` : undefined}>
                  <Tag color="blue">Đã chỉnh sửa</Tag>
                </Tooltip>
              ) : (
                <Tag>Mặc định</Tag>
              ),
          },
          {
            title: '',
            key: 'actions',
            width: 110,
            render: (_, t) => (
              <Space size={4}>
                <Button
                  type="text"
                  icon={<Pencil size={15} aria-hidden />}
                  aria-label={`Sửa mẫu ${t.name}`}
                  onClick={() => setEditing(t)}
                />
                {t.isCustomized && (
                  <Popconfirm
                    title="Khôi phục mẫu mặc định?"
                    description="Nội dung đã chỉnh sửa của sự kiện này sẽ bị bỏ."
                    onConfirm={async () => {
                      try {
                        await reset.mutateAsync(t.eventType);
                        message.success('Đã khôi phục mẫu mặc định');
                      } catch (e) {
                        onError(e);
                      }
                    }}
                  >
                    <Button
                      type="text"
                      icon={<RotateCcw size={15} aria-hidden />}
                      aria-label={`Khôi phục mẫu ${t.name}`}
                    />
                  </Popconfirm>
                )}
              </Space>
            ),
          },
        ]}
      />
      {editing && <EmailTemplateDialog template={editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function EmailTemplateDialog({ template, onClose }: { template: EmailTemplateDto; onClose: () => void }) {
  const [form] = Form.useForm<{ subject: string; bodyHtml: string }>();
  const [preview, setPreview] = useState<EmailPreviewDto | null>(null);
  const [previewing, setPreviewing] = useState(false);
  const body = useRef<TextAreaRef>(null);
  const { message } = App.useApp();
  const onError = useApiError();
  const save = useAction<{ subject: string; bodyHtml: string }>(RESOURCE, (b) => ({
    url: url(template.eventType),
    method: 'put',
    body: b,
  }));

  const showPreview = async () => {
    setPreviewing(true);
    try {
      const values = await form.validateFields();
      setPreview(await previewEmailTemplate(template.eventType, values));
    } catch (e) {
      if (!(e && typeof e === 'object' && 'errorFields' in e)) onError(e);
    } finally {
      setPreviewing(false);
    }
  };

  /** Inserts {{Key}} at the cursor of the body. */
  const insert = (key: string) => {
    const el = body.current?.resizableTextArea?.textArea;
    const current = form.getFieldValue('bodyHtml') ?? '';
    const token = `{{${key}}}`;
    const at = el ? el.selectionStart : current.length;
    const end = el ? el.selectionEnd : current.length;
    form.setFieldValue('bodyHtml', current.slice(0, at) + token + current.slice(end));
    requestAnimationFrame(() => {
      el?.focus();
      el?.setSelectionRange(at + token.length, at + token.length);
    });
  };

  return (
    <Modal
      open
      width={1080}
      title={`Mẫu email: ${template.name}`}
      okText="Lưu mẫu"
      cancelText="Đóng"
      confirmLoading={save.isPending}
      onCancel={onClose}
      onOk={async () => {
        try {
          await save.mutateAsync(await form.validateFields());
          message.success('Đã lưu mẫu email');
          onClose();
        } catch (e) {
          if (!(e && typeof e === 'object' && 'errorFields' in e)) onError(e);
        }
      }}
    >
      <div className="email-tpl">
        <Form
          form={form}
          layout="vertical"
          className="email-tpl__form"
          initialValues={{ subject: template.subject, bodyHtml: template.bodyHtml }}
        >
          <Form.Item name="subject" label="Tiêu đề" rules={[{ required: true, whitespace: true }]}>
            <Input maxLength={500} />
          </Form.Item>
          <Form.Item
            name="bodyHtml"
            label="Nội dung (HTML)"
            rules={[{ required: true, whitespace: true }]}
            extra="{{#Body}}…{{/Body}}: đoạn chỉ hiện khi biến có giá trị."
          >
            <Input.TextArea ref={body} rows={14} className="email-tpl__body" spellCheck={false} />
          </Form.Item>
          <div className="email-tpl__vars">
            <div className="email-tpl__vars-title">Biến dùng được (bấm để chèn)</div>
            {template.placeholders.map((p) => (
              <Tooltip key={p.key} title={p.description}>
                <Tag className="email-tpl__var" onClick={() => insert(p.key)}>
                  {`{{${p.key}}}`}
                </Tag>
              </Tooltip>
            ))}
          </div>
        </Form>
        <div className="email-tpl__preview">
          <Space className="email-tpl__preview-head">
            <span>Xem trước với dữ liệu mẫu</span>
            <Button
              size="small"
              icon={<Eye size={14} aria-hidden />}
              loading={previewing}
              onClick={showPreview}
            >
              Xem trước
            </Button>
          </Space>
          {preview ? (
            <>
              <div className="email-tpl__subject">{preview.subject}</div>
              <iframe title="Xem trước email" className="email-tpl__frame" sandbox="" srcDoc={preview.html} />
            </>
          ) : (
            <div className="email-tpl__empty">Bấm "Xem trước" để xem email sẽ gửi.</div>
          )}
        </div>
      </div>
    </Modal>
  );
}
