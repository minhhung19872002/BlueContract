import {
  Alert,
  App,
  Button,
  Card,
  Descriptions,
  Drawer,
  Form,
  Input,
  List,
  Modal,
  Select,
  Space,
  Switch,
  Table,
  Tabs,
  Tag,
  Typography,
  Upload,
} from 'antd';
import { EyeOutlined, FileAddOutlined, InboxOutlined, PlusOutlined, UploadOutlined } from '@ant-design/icons';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  downloadTemplate,
  downloadTemplatePreviewDocx,
  fetchTemplatePreview,
  useActivateTemplateVersion,
  useCreateSampleDocument,
  useCreateTemplate,
  useTemplate,
  useTemplateFields,
  useTemplates,
  useUpdateTemplate,
  useUploadTemplateVersion,
  useValidateTemplate,
  type TemplateKind,
  type TemplateListItemDto,
  type TemplateValidationResult,
} from '../../api/documents';
import { PdfViewer } from '../../components/PdfViewer';
import { StatusTag, statusLabel, statusOptions } from '../../components/StatusTag';
import { PageHeader, useApiError } from '../../components/common';
import { formatDateTime } from '../../utils/format';
import { ExportableTable } from '../../components/TableExportButton';

function ValidationResult({ result }: { result: TemplateValidationResult }) {
  if (result.isValid) {
    return <Alert type="success" showIcon message={`Mẫu hợp lệ — ${result.tags.length} thẻ placeholder.`} />;
  }
  return (
    <Alert
      type="error"
      showIcon
      message={`Có ${result.errors.length} lỗi placeholder`}
      description={
        <List
          size="small"
          dataSource={result.errors}
          renderItem={(e) => (
            <List.Item>
              <Typography.Text code>{`{{${e.tag}}}`}</Typography.Text> {e.message}
            </List.Item>
          )}
        />
      }
    />
  );
}

/** Upload a .docx, validate placeholders first, then save as a new template or a new version. */
function UploadTemplateModal({
  open,
  onClose,
  templateId,
  kind: fixedKind,
}: {
  open: boolean;
  onClose: () => void;
  templateId?: string;
  kind?: TemplateKind;
}) {
  const [form] = Form.useForm();
  const [file, setFile] = useState<File | null>(null);
  const [validation, setValidation] = useState<TemplateValidationResult | null>(null);
  const validate = useValidateTemplate();
  const create = useCreateTemplate();
  const upload = useUploadTemplateVersion(templateId ?? '');
  const { message } = App.useApp();
  const onError = useApiError();
  const kind = (Form.useWatch('kind', form) as TemplateKind | undefined) ?? fixedKind;

  const runValidation = async (f: File, k: TemplateKind | undefined) => {
    setValidation(null);
    if (!k) return;
    try {
      setValidation(await validate.mutateAsync({ kind: k, file: f }));
    } catch (e) {
      onError(e);
    }
  };

  const close = () => {
    form.resetFields();
    setFile(null);
    setValidation(null);
    onClose();
  };

  return (
    <Modal
      title={templateId ? 'Tải lên phiên bản mới' : 'Thêm mẫu biểu'}
      open={open}
      onCancel={close}
      okText="Lưu"
      cancelText="Hủy"
      okButtonProps={{ disabled: !validation?.isValid, loading: create.isPending || upload.isPending }}
      onOk={() => form.submit()}
      width={720}
      destroyOnHidden
    >
      <Form
        form={form}
        layout="vertical"
        initialValues={{ kind: fixedKind ?? 'Contract' }}
        onFinish={async (v: {
          code: string;
          name: string;
          kind: TemplateKind;
          description?: string;
          note?: string;
        }) => {
          if (!file) return;
          try {
            if (templateId) {
              const version = await upload.mutateAsync({ file, note: v.note });
              message.success(`Đã tải lên phiên bản ${version} và kích hoạt`);
            } else {
              await create.mutateAsync({ ...v, file });
              message.success('Đã thêm mẫu biểu');
            }
            close();
          } catch (e) {
            onError(e);
          }
        }}
      >
        {!templateId && (
          <>
            <Form.Item
              name="code"
              label="Mã mẫu"
              rules={[
                { required: true, pattern: /^[A-Za-z0-9_]{2,50}$/, message: 'Chữ không dấu, số, gạch dưới' },
              ]}
            >
              <Input placeholder="VD: HD_THUOC_2027" />
            </Form.Item>
            <Form.Item name="name" label="Tên mẫu" rules={[{ required: true, message: 'Bắt buộc' }]}>
              <Input />
            </Form.Item>
            <Form.Item
              name="kind"
              label="Loại mẫu (quyết định placeholder hợp lệ)"
              rules={[{ required: true }]}
            >
              <Select
                options={statusOptions('templateKind')}
                onChange={(k: TemplateKind) => file && runValidation(file, k)}
              />
            </Form.Item>
            <Form.Item name="description" label="Mô tả">
              <Input.TextArea rows={2} />
            </Form.Item>
          </>
        )}
        {templateId && (
          <Form.Item name="note" label="Ghi chú thay đổi">
            <Input />
          </Form.Item>
        )}
        <Form.Item label="Tệp mẫu (.docx)" required>
          <Upload.Dragger
            accept=".docx"
            maxCount={1}
            beforeUpload={(f) => {
              setFile(f);
              void runValidation(f, kind);
              return false;
            }}
            onRemove={() => {
              setFile(null);
              setValidation(null);
            }}
          >
            <p className="ant-upload-drag-icon">
              <InboxOutlined />
            </p>
            <p>Kéo thả hoặc bấm để chọn tệp Word (tối đa 20 MB)</p>
          </Upload.Dragger>
        </Form.Item>
        {validate.isPending && <Alert type="info" message="Đang kiểm tra placeholder..." />}
        {validation && <ValidationResult result={validation} />}
      </Form>
    </Modal>
  );
}

function FieldsCatalog({ kind }: { kind: TemplateKind }) {
  const fields = useTemplateFields(kind);
  if (!fields.data) return null;
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Alert
        type="info"
        showIcon
        message="Cú pháp"
        description={
          <>
            <Typography.Text code>{'{{Contract.Number}}'}</Typography.Text> giá trị ·{' '}
            <Typography.Text code>{'{{Contract.TotalAmount | money}}'}</Typography.Text> bộ lọc (
            {fields.data.filters.join(', ')}) ·{' '}
            <Typography.Text code>{'{{#Items}}...{{/Items}}'}</Typography.Text> lặp (đặt trong một dòng bảng
            để lặp dòng) · <Typography.Text code>{'{{@index}}'}</Typography.Text> số thứ tự ·{' '}
            <Typography.Text code>{'{{#if Path}}...{{/if}}'}</Typography.Text> điều kiện ·{' '}
            <Typography.Text code>{'{{SIG_DIRECTOR}}'}</Typography.Text> vị trí chữ ký.
          </>
        }
      />
      <Table
        size="small"
        rowKey="path"
        pagination={false}
        dataSource={fields.data.fields}
        columns={[
          {
            title: 'Placeholder',
            dataIndex: 'path',
            render: (p: string) => <Typography.Text code copyable>{`{{${p}}}`}</Typography.Text>,
          },
          { title: 'Ý nghĩa', dataIndex: 'description' },
        ]}
      />
      {fields.data.collections.map((c) => (
        <Card
          key={c.name}
          size="small"
          title={<Typography.Text code>{`{{#${c.name}}} … {{/${c.name}}}`}</Typography.Text>}
          extra={c.description}
        >
          <Space wrap>
            {c.fields.map((f) => (
              <Tag key={f.path} title={f.description}>{`{{${f.path}}}`}</Tag>
            ))}
          </Space>
        </Card>
      ))}
      <Card size="small" title="Vị trí chữ ký">
        <Space wrap>
          {fields.data.signatureAnchors.map((a) => (
            <Tag color="purple" key={a}>{`{{${a}}}`}</Tag>
          ))}
        </Space>
      </Card>
    </Space>
  );
}

function TemplateDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const detail = useTemplate(id);
  const activate = useActivateTemplateVersion(id ?? '');
  const update = useUpdateTemplate(id ?? '');
  const createSample = useCreateSampleDocument();
  const [uploading, setUploading] = useState(false);
  const [preview, setPreview] = useState<number | null>(null);
  const navigate = useNavigate();
  const { message } = App.useApp();
  const onError = useApiError();
  const t = detail.data;

  return (
    <Drawer
      title={t?.name ?? 'Mẫu biểu'}
      open={!!id}
      onClose={onClose}
      width={900}
      loading={detail.isLoading}
      destroyOnHidden
    >
      {t && (
        <>
          <Descriptions size="small" column={{ xs: 1, md: 2 }} style={{ marginBottom: 12 }}>
            <Descriptions.Item label="Mã">{t.code}</Descriptions.Item>
            <Descriptions.Item label="Loại">
              <StatusTag domain="templateKind" value={t.kind} />
            </Descriptions.Item>
            <Descriptions.Item label="Nguồn">
              {t.isBuiltIn ? 'Mẫu chuẩn của hệ thống' : 'Do bệnh viện tải lên'}
            </Descriptions.Item>
            <Descriptions.Item label="Sử dụng">
              <Switch
                checked={t.isActive}
                onChange={(v) =>
                  update.mutateAsync({ name: t.name, description: t.description, isActive: v }).catch(onError)
                }
              />
            </Descriptions.Item>
            <Descriptions.Item label="Mô tả" span={2}>
              {t.description}
            </Descriptions.Item>
          </Descriptions>
          <Space wrap style={{ marginBottom: 12 }}>
            <Button type="primary" icon={<EyeOutlined />} onClick={() => setPreview(t.activeVersionNumber)}>
              Xem trước với dữ liệu mẫu
            </Button>
            <Button
              icon={<FileAddOutlined />}
              loading={createSample.isPending}
              onClick={async () => {
                try {
                  const docId = await createSample.mutateAsync({ templateId: t.id, title: null });
                  message.success('Đã tạo tài liệu thử');
                  navigate(`/tai-lieu/${docId}`);
                } catch (e) {
                  onError(e);
                }
              }}
            >
              Tạo tài liệu thử (sửa trực tuyến)
            </Button>
            <Button icon={<UploadOutlined />} onClick={() => setUploading(true)}>
              Tải lên phiên bản mới
            </Button>
            <Button
              onClick={() => downloadTemplatePreviewDocx(t.id, `${t.code}_xem-truoc.docx`).catch(onError)}
            >
              Tải DOCX xem trước
            </Button>
          </Space>
          <Tabs
            items={[
              {
                key: 'versions',
                label: `Phiên bản (${t.versions.length})`,
                children: (
                  <Table
                    size="small"
                    rowKey="id"
                    pagination={false}
                    dataSource={t.versions}
                    scroll={{ x: 'max-content' }}
                    columns={[
                      {
                        title: 'Phiên bản',
                        dataIndex: 'versionNumber',
                        render: (v: number, r) => (
                          <Space>
                            v{v}
                            {r.isActive && <Tag color="green">Đang dùng</Tag>}
                          </Space>
                        ),
                      },
                      {
                        title: 'Tải lên',
                        dataIndex: 'uploadedAt',
                        render: (v: string, r) => `${formatDateTime(v)} · ${r.uploadedByName ?? ''}`,
                      },
                      { title: 'Ghi chú', dataIndex: 'note' },
                      {
                        title: 'SHA-256',
                        dataIndex: 'sha256',
                        render: (v: string) => (
                          <Typography.Text code copyable={{ text: v }}>
                            {v.slice(0, 12)}…
                          </Typography.Text>
                        ),
                      },
                      {
                        title: '',
                        key: 'actions',
                        render: (_, r) => (
                          <Space>
                            <Button size="small" onClick={() => setPreview(r.versionNumber)}>
                              Xem
                            </Button>
                            <Button
                              size="small"
                              onClick={() =>
                                downloadTemplate(
                                  t.id,
                                  r.versionNumber,
                                  `${t.code}_v${r.versionNumber}.docx`,
                                ).catch(onError)
                              }
                            >
                              Tải
                            </Button>
                            {!r.isActive && (
                              <Button
                                size="small"
                                onClick={() =>
                                  activate
                                    .mutateAsync(r.versionNumber)
                                    .then(() => message.success('Đã kích hoạt'), onError)
                                }
                              >
                                Dùng phiên bản này
                              </Button>
                            )}
                          </Space>
                        ),
                      },
                    ]}
                  />
                ),
              },
              {
                key: 'placeholders',
                label: `Placeholder trong mẫu (${t.placeholders.length})`,
                children: (
                  <Space wrap>
                    {t.placeholders.map((p) => (
                      <Tag key={p}>{`{{${p}}}`}</Tag>
                    ))}
                  </Space>
                ),
              },
              {
                key: 'catalog',
                label: 'Danh sách placeholder hợp lệ',
                children: <FieldsCatalog kind={t.kind} />,
              },
            ]}
          />
          <UploadTemplateModal
            open={uploading}
            onClose={() => setUploading(false)}
            templateId={t.id}
            kind={t.kind}
          />
          <Modal
            title={`Xem trước — ${t.name} (v${preview ?? ''})`}
            open={preview !== null}
            onCancel={() => setPreview(null)}
            footer={null}
            width={960}
            destroyOnHidden
          >
            {preview !== null && (
              <PdfViewer load={() => fetchTemplatePreview(t.id, preview)} cacheKey={`${t.id}-${preview}`} />
            )}
          </Modal>
        </>
      )}
    </Drawer>
  );
}

/** Quản lý mẫu biểu (spec §9.1-14, §10). */
export function TemplatesPage() {
  const [kind, setKind] = useState<TemplateKind | undefined>();
  const list = useTemplates(kind);
  const [selected, setSelected] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const rows = useMemo(() => list.data ?? [], [list.data]);

  return (
    <>
      <PageHeader
        title="Mẫu biểu"
        subtitle="Mẫu DOCX dùng sinh hợp đồng, biên bản, phiếu… Mỗi lần tải lên được kiểm tra placeholder và lưu phiên bản."
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
            Thêm mẫu biểu
          </Button>
        }
      />
      <Card>
        <Space style={{ marginBottom: 12 }}>
          <Select
            allowClear
            placeholder="Loại mẫu"
            style={{ width: 240 }}
            options={statusOptions('templateKind')}
            value={kind}
            onChange={setKind}
          />
        </Space>
        <ExportableTable<TemplateListItemDto>
          exportTitle="Mẫu biểu"
          rowKey="id"
          size="middle"
          loading={list.isLoading}
          dataSource={rows}
          pagination={false}
          scroll={{ x: 'max-content' }}
          onRow={(r) => ({ onClick: () => setSelected(r.id), style: { cursor: 'pointer' } })}
          columns={[
            { title: 'Mã', dataIndex: 'code', width: 190 },
            { title: 'Tên mẫu', dataIndex: 'name' },
            {
              title: 'Loại',
              dataIndex: 'kind',
              width: 200,
              render: (v: string) => <StatusTag domain="templateKind" value={v} />,
            },
            {
              title: 'Phiên bản',
              dataIndex: 'activeVersionNumber',
              width: 110,
              render: (v: number, r) => `v${v} / ${r.versionCount}`,
            },
            {
              title: 'Nguồn',
              dataIndex: 'isBuiltIn',
              width: 120,
              render: (v: boolean) => (v ? <Tag color="blue">Mẫu chuẩn</Tag> : <Tag>Tải lên</Tag>),
            },
            {
              title: 'Trạng thái',
              dataIndex: 'isActive',
              width: 150,
              render: (v: boolean) => <StatusTag domain="active" value={v} />,
            },
            {
              title: 'Cập nhật',
              dataIndex: 'lastUploadedAt',
              width: 160,
              render: (v?: string) => formatDateTime(v),
            },
          ]}
        />
        <Typography.Paragraph type="secondary" style={{ marginTop: 12 }}>
          {rows.length} mẫu{kind ? ` loại ${statusLabel('templateKind', kind)}` : ''}. Bấm vào một dòng để xem
          phiên bản, placeholder và xem trước.
        </Typography.Paragraph>
      </Card>
      <TemplateDrawer id={selected} onClose={() => setSelected(null)} />
      <UploadTemplateModal open={creating} onClose={() => setCreating(false)} />
    </>
  );
}
