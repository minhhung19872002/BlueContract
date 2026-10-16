import {
  Alert,
  App,
  Button,
  Card,
  Col,
  Descriptions,
  Dropdown,
  Form,
  Modal,
  Row,
  Select,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
  Upload,
} from 'antd';
import {
  DownloadOutlined,
  EditOutlined,
  EyeOutlined,
  FilePdfOutlined,
  LockOutlined,
  SafetyCertificateOutlined,
  SendOutlined,
  UploadOutlined,
} from '@ant-design/icons';
import { useEffect, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import {
  downloadDocumentFile,
  fetchDocumentFile,
  useDocument,
  useRenderPdf,
  useStartEditSession,
  useUploadDocumentVersion,
  type DocumentVersionDto,
  type EditorMode,
  type EditorSessionDto,
} from '../../api/documents';
import { useLookup } from '../../api/admin';
import {
  useDocumentWorkflow,
  useStartWorkflow,
  useWorkflowTemplates,
  verifyVersion,
  type VerifyResultDto,
} from '../../api/signing';
import { useAuth } from '../../auth/useAuth';
import { P } from '../../auth/permissions';
import { WorkflowCard } from '../signing/WorkflowPanel';
import { SignatureTable } from '../signing/VerifySignaturePage';
import { DocEditor } from '../../components/DocEditor';
import { PdfViewer } from '../../components/PdfViewer';
import { StatusTag } from '../../components/StatusTag';
import { PageHeader, useApiError } from '../../components/common';
import { formatDateTime } from '../../utils/format';

function formatSize(bytes?: number | null) {
  if (!bytes) return '';
  return bytes > 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.ceil(bytes / 1024)} KB`;
}

function Hash({ value }: { value?: string | null }) {
  if (!value) return null;
  return (
    <Tooltip title={value}>
      <Typography.Text code copyable={{ text: value }} style={{ fontSize: 12 }}>
        {value.slice(0, 10)}…
      </Typography.Text>
    </Tooltip>
  );
}

/** "Gửi ký": picks the signing workflow (default for the document type) and, for sample documents, the supplier party. */
function StartWorkflowDialog({
  documentId,
  documentType,
  open,
  onClose,
}: {
  documentId: string;
  documentType: string;
  open: boolean;
  onClose: () => void;
}) {
  const { can } = useAuth();
  const isSample = documentType === 'TemplateSample';
  // Sample documents may try any workflow (administrators testing a configuration).
  const templates = useWorkflowTemplates(undefined, open && can(P.WorkflowManage, P.TemplateManage));
  const suppliers = useLookup('suppliers', undefined, open && isSample);
  const start = useStartWorkflow();
  const onError = useApiError();
  const { message } = App.useApp();
  const [form] = Form.useForm<{ templateId?: string; supplierId?: string }>();
  const options = (templates.data ?? [])
    .filter((t) => t.isActive && (isSample || t.documentType === documentType))
    .map((t) => ({ value: t.id, label: ` (${t.steps.length} bước)` }));

  return (
    <Modal
      title="Gửi ký"
      open={open}
      onCancel={onClose}
      okText="Gửi ký"
      okButtonProps={{ loading: start.isPending, icon: <SendOutlined /> }}
      onOk={async () => {
        const v = await form.validateFields();
        try {
          await start.mutateAsync({
            documentId,
            templateId: v.templateId ?? null,
            supplierId: v.supplierId ?? null,
            facilityId: null,
            domain: null,
          });
          message.success('Đã gửi ký; người xử lý bước đầu tiên đã được thông báo');
          onClose();
        } catch (e) {
          onError(e);
        }
      }}
      destroyOnHidden
    >
      <Form form={form} layout="vertical">
        <Form.Item
          name="templateId"
          label="Luồng ký"
          extra="Để trống để dùng luồng mặc định của loại tài liệu"
          rules={isSample ? [{ required: true, message: 'Chọn luồng ký' }] : []}
        >
          <Select allowClear options={options} loading={templates.isLoading} placeholder="Luồng mặc định" />
        </Form.Item>
        {isSample && (
          <Form.Item name="supplierId" label="Nhà cung cấp tham gia ký (nếu luồng có bước NCC)">
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              options={(suppliers.data ?? []).map((s) => ({ value: s.id, label: s.name }))}
            />
          </Form.Item>
        )}
      </Form>
    </Modal>
  );
}

/** Document detail: versions (SHA-256), PDF rendition, online editing with ONLYOFFICE, signing workflow. */
export function DocumentPage() {
  const { id } = useParams<{ id: string }>();
  const [awaitingSave, setAwaitingSave] = useState(false);
  const doc = useDocument(id, awaitingSave);
  const renderPdf = useRenderPdf(id ?? '');
  const startEdit = useStartEditSession(id ?? '');
  const upload = useUploadDocumentVersion(id ?? '');
  const [session, setSession] = useState<EditorSessionDto | null>(null);
  const [viewing, setViewing] = useState<DocumentVersionDto | null>(null);
  const [starting, setStarting] = useState(false);
  const [verified, setVerified] = useState<{ version: number; result: VerifyResultDto } | null>(null);
  const workflow = useDocumentWorkflow(id);
  const wf = workflow.data;
  const { message } = App.useApp();
  const onError = useApiError();
  const d = doc.data;
  const versionCountWhenOpened = useRef(0);

  const openEditor = async (mode: EditorMode) => {
    try {
      versionCountWhenOpened.current = d?.versions.length ?? 0;
      setSession(await startEdit.mutateAsync(mode));
    } catch (e) {
      onError(e);
    }
  };

  const closeEditor = () => {
    setSession(null);
    // ONLYOFFICE saves a few seconds after the last editor disconnects: poll until the new version shows up.
    setAwaitingSave(true);
    setTimeout(() => setAwaitingSave(false), 30_000);
  };

  const versionCount = d?.versions.length ?? 0;
  useEffect(() => {
    if (awaitingSave && versionCount > versionCountWhenOpened.current) {
      setAwaitingSave(false);
      message.success('Đã lưu phiên bản mới từ trình soạn thảo');
    }
  }, [awaitingSave, versionCount, message]);

  const current = d?.versions.find((v) => v.isCurrent);
  const canStart =
    !!d?.canEdit && d.status === 'Draft' && (!wf || wf.status === 'Completed' || wf.status === 'Cancelled');

  return (
    <>
      <PageHeader
        title={d?.title ?? 'Tài liệu'}
        subtitle={
          d ? (
            <Space>
              <StatusTag domain="documentType" value={d.type} />
              <StatusTag domain="documentStatus" value={d.status} />
            </Space>
          ) : undefined
        }
        extra={
          d?.canEdit && (
            <Space wrap>
              {canStart && (
                <Button type="primary" icon={<SendOutlined />} onClick={() => setStarting(true)}>
                  Gửi ký
                </Button>
              )}
              <Dropdown.Button
                type={canStart ? 'default' : 'primary'}
                icon={<EditOutlined />}
                loading={startEdit.isPending}
                disabled={!current?.hasDocx}
                onClick={() => openEditor('Edit')}
                menu={{
                  items: [
                    { key: 'Review', label: 'Mở chế độ rà soát (bắt buộc theo dõi thay đổi)' },
                    { key: 'View', label: 'Chỉ xem' },
                  ],
                  onClick: ({ key }) => openEditor(key as EditorMode),
                }}
              >
                Sửa trực tuyến
              </Dropdown.Button>
              <Upload
                accept=".docx,.pdf"
                showUploadList={false}
                beforeUpload={(file) => {
                  upload
                    .mutateAsync({ file })
                    .then(() => message.success('Đã tải lên phiên bản mới'), onError);
                  return false;
                }}
              >
                <Button icon={<UploadOutlined />} loading={upload.isPending}>
                  Tải lên phiên bản
                </Button>
              </Upload>
            </Space>
          )
        }
      />
      {awaitingSave && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          message="Đang chờ trình soạn thảo lưu phiên bản mới..."
        />
      )}
      <Card loading={doc.isLoading} style={{ marginBottom: 16 }}>
        {d && (
          <Descriptions size="small" column={{ xs: 1, md: 3 }}>
            <Descriptions.Item label="Mẫu biểu">{d.templateName ?? '—'}</Descriptions.Item>
            <Descriptions.Item label="Phiên bản hiện tại">v{current?.versionNumber}</Descriptions.Item>
            <Descriptions.Item label="Số phiên bản">{d.versions.length}</Descriptions.Item>
          </Descriptions>
        )}
      </Card>
      {wf && (
        <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
          <Col span={24}>
            <WorkflowCard wf={wf} />
          </Col>
        </Row>
      )}
      <Card title="Các phiên bản">
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          message="Phiên bản đã có chữ ký số là bất biến. Mọi chỉnh sửa sau đó tạo phiên bản mới và luồng ký phải bắt đầu lại."
        />
        <Table<DocumentVersionDto>
          rowKey="id"
          size="middle"
          pagination={false}
          dataSource={d?.versions ?? []}
          scroll={{ x: 'max-content' }}
          columns={[
            {
              title: 'Phiên bản',
              dataIndex: 'versionNumber',
              render: (v: number, r) => (
                <Space>
                  v{v}
                  {r.isCurrent && <Tag color="blue">Hiện tại</Tag>}
                  {r.isLocked && (
                    <Tag icon={<LockOutlined />} color="purple">
                      {r.signatureCount} chữ ký
                    </Tag>
                  )}
                  {r.isFinal && <Tag color="green">Bản cuối</Tag>}
                </Space>
              ),
            },
            { title: 'Lý do', dataIndex: 'reason' },
            {
              title: 'Người tạo',
              dataIndex: 'createdByName',
              render: (v: string, r) => `${v ?? ''} · ${formatDateTime(r.createdAt)}`,
            },
            {
              title: 'DOCX',
              key: 'docx',
              render: (_, r) =>
                r.hasDocx ? (
                  <Space direction="vertical" size={0}>
                    <Hash value={r.docxSha256} />
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {formatSize(r.docxSize)}
                    </Typography.Text>
                  </Space>
                ) : (
                  '—'
                ),
            },
            {
              title: 'PDF',
              key: 'pdf',
              render: (_, r) =>
                r.hasPdf ? (
                  <Space direction="vertical" size={0}>
                    <Hash value={r.pdfSha256} />
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {formatSize(r.pdfSize)}
                    </Typography.Text>
                  </Space>
                ) : (
                  '—'
                ),
            },
            {
              title: '',
              key: 'actions',
              render: (_, r) => (
                <Space wrap>
                  {r.hasPdf ? (
                    <Button size="small" icon={<EyeOutlined />} onClick={() => setViewing(r)}>
                      Xem PDF
                    </Button>
                  ) : (
                    r.hasDocx &&
                    !r.isLocked && (
                      <Button
                        size="small"
                        icon={<FilePdfOutlined />}
                        loading={renderPdf.isPending && renderPdf.variables === r.id}
                        onClick={() =>
                          renderPdf.mutateAsync(r.id).then(() => message.success('Đã tạo bản PDF'), onError)
                        }
                      >
                        Tạo PDF
                      </Button>
                    )
                  )}
                  {r.signatureCount > 0 && (
                    <Button
                      size="small"
                      icon={<SafetyCertificateOutlined />}
                      onClick={() =>
                        verifyVersion(d!.id, r.id).then(
                          (result) => setVerified({ version: r.versionNumber, result }),
                          onError,
                        )
                      }
                    >
                      Xác minh chữ ký
                    </Button>
                  )}
                  {r.hasDocx && (
                    <Button
                      size="small"
                      icon={<DownloadOutlined />}
                      onClick={() =>
                        downloadDocumentFile(d!.id, r.id, 'docx', `tai-lieu_v${r.versionNumber}.docx`).catch(
                          onError,
                        )
                      }
                    >
                      DOCX
                    </Button>
                  )}
                  {r.hasPdf && (
                    <Button
                      size="small"
                      icon={<DownloadOutlined />}
                      onClick={() =>
                        downloadDocumentFile(d!.id, r.id, 'pdf', `tai-lieu_v${r.versionNumber}.pdf`).catch(
                          onError,
                        )
                      }
                    >
                      PDF
                    </Button>
                  )}
                </Space>
              ),
            },
          ]}
        />
      </Card>

      <Modal
        title={`Soạn thảo — ${d?.title ?? ''}`}
        open={!!session}
        onCancel={closeEditor}
        footer={
          <Button type="primary" onClick={closeEditor}>
            Đóng và lưu
          </Button>
        }
        width="96vw"
        style={{ top: 12 }}
        destroyOnHidden
      >
        {session && <DocEditor session={session} />}
      </Modal>

      {d && (
        <StartWorkflowDialog
          documentId={d.id}
          documentType={d.type}
          open={starting}
          onClose={() => setStarting(false)}
        />
      )}

      <Modal
        title={`Xác minh chữ ký — v${verified?.version ?? ''}`}
        open={!!verified}
        onCancel={() => setVerified(null)}
        footer={null}
        width={1100}
        destroyOnHidden
      >
        {verified && (
          <>
            <Alert
              showIcon
              style={{ marginBottom: 12 }}
              type={verified.result.allValid ? 'success' : 'error'}
              message={
                verified.result.allValid
                  ? `Tất cả ${verified.result.signatureCount} chữ ký đều toàn vẹn.`
                  : 'Có chữ ký không hợp lệ: nội dung đã bị thay đổi sau khi ký.'
              }
            />
            <SignatureTable result={verified.result} />
          </>
        )}
      </Modal>

      <Modal
        title={`PDF v${viewing?.versionNumber ?? ''}`}
        open={!!viewing}
        onCancel={() => setViewing(null)}
        footer={null}
        width={980}
        destroyOnHidden
      >
        {viewing && d && (
          <PdfViewer
            load={() => fetchDocumentFile(d.id, viewing.id, 'pdf')}
            cacheKey={`${viewing.id}:${viewing.pdfSha256}`}
          />
        )}
      </Modal>
    </>
  );
}
