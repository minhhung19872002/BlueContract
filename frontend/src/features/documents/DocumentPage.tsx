import {
  App,
  Button,
  Card,
  Dropdown,
  Empty,
  Form,
  Modal,
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
import { useParams, useSearchParams } from 'react-router-dom';
import { GitCompareArrows } from 'lucide-react';
import { CompareVersionsDialog } from './CompareVersionsDialog';
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
import { SignatureList, VerifyVerdictBanner } from '../signing/VerifySignaturePage';
import { DocEditor } from '../../components/DocEditor';
import { PdfViewer } from '../../components/PdfViewer';
import { StatusTag } from '../../components/StatusTag';
import { useApiError } from '../../components/common';
import { Banner, DetailGrid, DocHeader, Panel } from '../../components/layout';
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
  const [params, setParams] = useSearchParams();
  const compareOpen = params.get('compare') === '1';
  const setCompare = (open: boolean) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (open) next.set('compare', '1');
        else next.delete('compare');
        return next;
      },
      { replace: true },
    );
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

  const versionsTable = (
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
  );

  const actions = d && (
    <>
      {d.versions.length > 1 && (
        <Button icon={<GitCompareArrows size={16} aria-hidden />} onClick={() => setCompare(true)}>
          So sánh phiên bản
        </Button>
      )}
      {d.canEdit && canStart && (
        <Button type="primary" icon={<SendOutlined />} onClick={() => setStarting(true)}>
          Gửi ký
        </Button>
      )}
      {d.canEdit && (
        <>
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
              upload.mutateAsync({ file }).then(() => message.success('Đã tải lên phiên bản mới'), onError);
              return false;
            }}
          >
            <Button icon={<UploadOutlined />} loading={upload.isPending}>
              Tải lên phiên bản
            </Button>
          </Upload>
        </>
      )}
    </>
  );

  if (!d) return <Card loading={doc.isLoading} />;

  return (
    <>
      <DocHeader
        back={{ to: -1, label: 'Quay lại' }}
        plain
        code={d.title}
        tags={
          <>
            <StatusTag domain="documentType" value={d.type} />
            <StatusTag domain="documentStatus" value={d.status} />
          </>
        }
        sub={[
          d.templateName && `Mẫu: ${d.templateName}`,
          current && `Phiên bản hiện tại v${current.versionNumber}`,
          `${d.versions.length} phiên bản`,
        ]
          .filter(Boolean)
          .join(' · ')}
        actions={actions}
      />
      {awaitingSave && <Banner tone="info" title="Đang chờ trình soạn thảo lưu phiên bản mới…" />}
      <DetailGrid
        main={
          <>
            <Panel
              title={current ? `Nội dung — v${current.versionNumber}` : 'Nội dung'}
              flush
              extra={
                current?.signatureCount ? (
                  <Tag icon={<LockOutlined />} color="purple">
                    {current.signatureCount} chữ ký
                  </Tag>
                ) : undefined
              }
            >
              {current?.hasPdf ? (
                <PdfViewer
                  load={() => fetchDocumentFile(d.id, current.id, 'pdf')}
                  cacheKey={`${current.id}:${current.pdfSha256}`}
                  height="72vh"
                  fileName={`tai-lieu_v${current.versionNumber}.pdf`}
                />
              ) : (
                <Empty
                  style={{ padding: 32 }}
                  description={
                    current?.hasDocx
                      ? 'Phiên bản hiện tại chưa có bản PDF — bấm "Tạo PDF" ở bảng phiên bản.'
                      : 'Chưa có nội dung'
                  }
                />
              )}
            </Panel>
            <Panel title={`Các phiên bản (${d.versions.length})`} flush>
              <div style={{ padding: '12px 16px' }}>
                <Banner
                  tone="warning"
                  title="Phiên bản đã có chữ ký số là bất biến. Mọi chỉnh sửa sau đó tạo phiên bản mới và luồng ký phải bắt đầu lại."
                />
              </div>
              {versionsTable}
            </Panel>
          </>
        }
        side={
          wf ? (
            <WorkflowCard wf={wf} />
          ) : (
            <Panel title="Luồng ký">
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description={workflow.isLoading ? 'Đang tải…' : 'Tài liệu chưa được gửi ký'}
              />
            </Panel>
          )
        }
      />
      {compareOpen && (
        <CompareVersionsDialog
          documentId={d.id}
          versions={d.versions}
          open={compareOpen}
          onClose={() => setCompare(false)}
        />
      )}

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
          <div className="verify">
            <VerifyVerdictBanner result={verified.result} />
            {verified.result.signatures.length > 0 && <SignatureList result={verified.result} />}
          </div>
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
