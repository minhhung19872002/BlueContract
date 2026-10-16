import {
  Alert,
  App,
  Button,
  Card,
  Col,
  Empty,
  Input,
  List,
  Row,
  Space,
  Switch,
  Tag,
  Tree,
  Typography,
} from 'antd';
import {
  CheckCircleOutlined,
  DownloadOutlined,
  FileOutlined,
  FilePdfOutlined,
  FolderOpenOutlined,
} from '@ant-design/icons';
import { useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import type { DataNode } from 'antd/es/tree';
import { usePagedList } from '../../api/admin';
import { downloadFile } from '../../api/http';
import {
  ARCHIVE,
  archiveApi,
  useArchiveComplete,
  useArchiveTree,
  type ArchiveFileDto,
  type ArchiveNodeDto,
  type ArchiveSearchItemDto,
} from '../../api/reports';
import { useAuth } from '../../auth/useAuth';
import { P } from '../../auth/permissions';
import { StatusTag } from '../../components/StatusTag';
import { PageHeader, useApiError } from '../../components/common';
import { formatDate, formatDateTime } from '../../utils/format';
import { DocumentPdf } from '../proposals/ProposalDetailPage';

/** Hồ sơ lưu trữ (spec §9.1-12): tìm theo hợp đồng / số quyết định → cây tài liệu → xem file → tải ZIP + mục lục. */
export function ArchivePage() {
  const [params, setParams] = useSearchParams();
  const [search, setSearch] = useState(params.get('q') ?? '');
  const contractId = params.get('contract') ?? undefined;
  const results = usePagedList<ArchiveSearchItemDto>(ARCHIVE, {
    search: params.get('q') ?? undefined,
    pageSize: 50,
  });

  return (
    <>
      <PageHeader
        title="Hồ sơ lưu trữ"
        subtitle="Hồ sơ điện tử theo hợp đồng: từ quyết định thầu đến thanh lý, tải trọn bộ kèm mục lục và mã SHA-256"
      />
      <Row gutter={[16, 16]}>
        <Col xs={24} lg={7}>
          <Card>
            <Input.Search
              allowClear
              placeholder="Số hợp đồng, số quyết định, tên NCC"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onSearch={(v) => setParams((p) => (v ? (p.set('q', v), p) : (p.delete('q'), p)))}
              style={{ marginBottom: 12 }}
            />
            <List
              loading={results.isFetching}
              dataSource={results.data?.items ?? []}
              locale={{ emptyText: 'Không tìm thấy hồ sơ' }}
              renderItem={(r) => (
                <List.Item
                  onClick={() => setParams((p) => (p.set('contract', r.contractId), p))}
                  style={{
                    cursor: 'pointer',
                    background: r.contractId === contractId ? '#e6f4ff' : undefined,
                    paddingInline: 8,
                    borderRadius: 6,
                  }}
                >
                  <List.Item.Meta
                    title={
                      <Space size={4} wrap>
                        <span>{r.contractNumber}</span>
                        {r.isArchiveComplete && <CheckCircleOutlined style={{ color: '#52c41a' }} />}
                      </Space>
                    }
                    description={
                      <Space direction="vertical" size={0}>
                        <Typography.Text type="secondary" ellipsis style={{ maxWidth: 260 }}>
                          {r.supplierName}
                        </Typography.Text>
                        <Space size={4}>
                          <StatusTag domain="contractStatus" value={r.status} />
                          <Typography.Text type="secondary">{r.documentCount} tài liệu</Typography.Text>
                        </Space>
                      </Space>
                    }
                  />
                </List.Item>
              )}
            />
          </Card>
        </Col>
        <Col xs={24} lg={17}>
          {contractId ? (
            <Dossier contractId={contractId} />
          ) : (
            <Card>
              <Empty description="Chọn một hồ sơ hợp đồng" />
            </Card>
          )}
        </Col>
      </Row>
    </>
  );
}

function Dossier({ contractId }: { contractId: string }) {
  const tree = useArchiveTree(contractId);
  const t = tree.data;
  const [selected, setSelected] = useState<ArchiveNodeDto | null>(null);
  const [downloading, setDownloading] = useState(false);
  const navigate = useNavigate();
  const { can } = useAuth();
  const { message } = App.useApp();
  const onError = useApiError();
  const complete = useArchiveComplete(contractId);

  const nodes = useMemo<DataNode[]>(() => {
    const map = (n: ArchiveNodeDto): DataNode => ({
      key: n.key,
      title: (
        <Space size={4}>
          <span>{n.title}</span>
          {n.kind === 'Folder' ? (
            <Typography.Text type="secondary">({n.children.length})</Typography.Text>
          ) : (
            n.date && <Typography.Text type="secondary">{formatDate(n.date)}</Typography.Text>
          )}
          {n.files.length > 0 && <Tag>{n.files.length} tệp</Tag>}
        </Space>
      ),
      icon:
        n.kind === 'Folder' ? <FolderOpenOutlined /> : n.documentId ? <FilePdfOutlined /> : <FileOutlined />,
      children: n.children.map(map),
      isLeaf: n.kind !== 'Folder',
    });
    return (t?.folders ?? []).map(map);
  }, [t]);

  if (!t) return <Card loading={tree.isLoading} />;
  const find = (key: string, list: ArchiveNodeDto[]): ArchiveNodeDto | null =>
    list.reduce<ArchiveNodeDto | null>((hit, n) => hit ?? (n.key === key ? n : find(key, n.children)), null);
  const open = (file: ArchiveFileDto) =>
    archiveApi.fileLink(contractId, file.id).then((l) => window.open(l.url, '_blank', 'noopener'), onError);

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <Card
        title={
          <Space wrap>
            <span>Hồ sơ hợp đồng {t.contractNumber}</span>
            <StatusTag domain="contractStatus" value={t.status} />
          </Space>
        }
        extra={
          <Space wrap>
            {can(P.ArchiveDownload) && (
              <Switch
                checkedChildren="Đã đầy đủ"
                unCheckedChildren="Chưa đầy đủ"
                checked={t.isArchiveComplete}
                loading={complete.isPending}
                onChange={(v) =>
                  complete
                    .mutateAsync(v)
                    .then(() => message.success(v ? 'Đã đánh dấu hồ sơ đầy đủ' : 'Đã bỏ đánh dấu'), onError)
                }
              />
            )}
            {can(P.ArchiveDownload) && (
              <Button
                type="primary"
                icon={<DownloadOutlined />}
                loading={downloading}
                onClick={async () => {
                  setDownloading(true);
                  try {
                    await downloadFile(
                      `${ARCHIVE}/${contractId}/zip`,
                      `ho-so-${t.contractNumber.replace(/[^\w-]+/g, '-')}.zip`,
                      { timeout: 600_000 },
                    );
                    message.success('Đã tải hồ sơ (kèm muc_luc.pdf)');
                  } catch (e) {
                    onError(e);
                  } finally {
                    setDownloading(false);
                  }
                }}
              >
                Tải toàn bộ hồ sơ (ZIP)
              </Button>
            )}
          </Space>
        }
      >
        <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
          {t.contractName} — {t.supplierName}
          {t.bidDecisionNumber ? ` — QĐ ${t.bidDecisionNumber}` : ' — ngoài thầu'} · {t.fileCount} tệp
        </Typography.Paragraph>
        {t.isArchiveComplete && (
          <Alert
            type="success"
            showIcon
            message={`Hồ sơ đã được xác nhận đầy đủ bởi ${t.archiveCompletedByName} lúc ${formatDateTime(t.archiveCompletedAt)}`}
          />
        )}
      </Card>
      <Row gutter={[16, 16]}>
        <Col xs={24} xl={12}>
          <Card title="Cây tài liệu">
            <Tree
              showIcon
              defaultExpandAll
              treeData={nodes}
              onSelect={(keys) => setSelected(keys[0] ? find(String(keys[0]), t.folders) : null)}
            />
          </Card>
        </Col>
        <Col xs={24} xl={12}>
          <Card
            title={selected?.title ?? 'Xem tài liệu'}
            extra={
              selected?.link && (
                <Button size="small" onClick={() => navigate(selected.link!)}>
                  Mở chi tiết
                </Button>
              )
            }
          >
            {!selected || selected.kind === 'Folder' ? (
              <Empty description="Chọn tài liệu trong cây để xem" />
            ) : (
              <Space direction="vertical" style={{ width: '100%' }}>
                <List
                  size="small"
                  dataSource={selected.files}
                  locale={{ emptyText: 'Tài liệu chưa có tệp' }}
                  renderItem={(f) => (
                    <List.Item
                      actions={[
                        <a key="open" onClick={() => open(f)}>
                          Tải / xem
                        </a>,
                      ]}
                    >
                      <List.Item.Meta
                        title={`${f.fileName} — ${f.kind}`}
                        description={
                          f.sha256 ? (
                            <Typography.Text code copyable style={{ fontSize: 11 }}>
                              {f.sha256}
                            </Typography.Text>
                          ) : null
                        }
                      />
                    </List.Item>
                  )}
                />
                {selected.documentId && <DocumentPdf documentId={selected.documentId} />}
              </Space>
            )}
          </Card>
        </Col>
      </Row>
    </Space>
  );
}
