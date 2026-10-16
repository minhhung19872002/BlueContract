import {
  Alert,
  App,
  Button,
  Card,
  Descriptions,
  Dropdown,
  Modal,
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

/** Document detail: versions (SHA-256), PDF rendition, online editing with ONLYOFFICE. */
export function DocumentPage() {
  const { id } = useParams<{ id: string }>();
  const [awaitingSave, setAwaitingSave] = useState(false);
  const doc = useDocument(id, awaitingSave);
  const renderPdf = useRenderPdf(id ?? '');
  const startEdit = useStartEditSession(id ?? '');
  const upload = useUploadDocumentVersion(id ?? '');
  const [session, setSession] = useState<EditorSessionDto | null>(null);
  const [viewing, setViewing] = useState<DocumentVersionDto | null>(null);
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
              <Dropdown.Button
                type="primary"
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

      <Modal
        title={`PDF v${viewing?.versionNumber ?? ''}`}
        open={!!viewing}
        onCancel={() => setViewing(null)}
        footer={null}
        width={980}
        destroyOnHidden
      >
        {viewing && d && (
          <PdfViewer load={() => fetchDocumentFile(d.id, viewing.id, 'pdf')} cacheKey={viewing.id} />
        )}
      </Modal>
    </>
  );
}
