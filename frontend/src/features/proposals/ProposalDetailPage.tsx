import { App, Button, Card, Col, Descriptions, Empty, Popconfirm, Row, Table, Tabs } from 'antd';
import { EditOutlined, SendOutlined, StopOutlined } from '@ant-design/icons';
import { useNavigate, useParams } from 'react-router-dom';
import { fetchDocumentFile, useDocument } from '../../api/documents';
import {
  domainLabel,
  proposalApi,
  useProposal,
  useProposalAction,
  type ProposalLineDto,
} from '../../api/proposals';
import { useDocumentWorkflow } from '../../api/signing';
import { PdfViewer } from '../../components/PdfViewer';
import { StatusTag } from '../../components/StatusTag';
import { useApiError } from '../../components/common';
import { DocHeader } from '../../components/layout';
import { formatQuantity } from '../../utils/format';
import { WorkflowCard } from '../signing/WorkflowPanel';

/** Current PDF of a business document (shared by proposal, summary and call order pages). */
export function DocumentPdf({ documentId }: { documentId: string }) {
  const doc = useDocument(documentId);
  const current = doc.data?.versions.find((v) => v.isCurrent);
  return current?.hasPdf ? (
    <PdfViewer
      load={() => fetchDocumentFile(documentId, current.id, 'pdf')}
      cacheKey={`${current.id}:${current.pdfSha256}`}
      height="70vh"
    />
  ) : (
    <Empty description={doc.isLoading ? 'Đang tải...' : 'Tài liệu chưa có bản PDF (được tạo khi gửi ký)'} />
  );
}

export function ProposalDetailPage() {
  const { id } = useParams<{ id: string }>();
  const proposal = useProposal(id);
  const p = proposal.data;
  const workflow = useDocumentWorkflow(p?.documentId ?? undefined);
  const navigate = useNavigate();
  const { message } = App.useApp();
  const onError = useApiError();
  const submit = useProposalAction(() => proposalApi.submit(id!));
  const cancel = useProposalAction(() => proposalApi.cancel(id!));

  if (!p) return <Card loading={proposal.isLoading} />;

  return (
    <>
      <DocHeader
        back={{ to: -1, label: 'Quay lại' }}
        plain
        code={
          <>
            Phiếu đề xuất <span className="mono">{p.number}</span>
          </>
        }
        tags={<StatusTag domain="proposalStatus" value={p.status} />}
        actions={
          <>
            {p.canEdit && (
              <Button icon={<EditOutlined />} onClick={() => navigate(`/de-xuat/${p.id}/sua`)}>
                Sửa
              </Button>
            )}
            {p.canSubmit && (
              <Button
                type="primary"
                icon={<SendOutlined />}
                loading={submit.isPending}
                onClick={() =>
                  submit
                    .mutateAsync()
                    .then(() => message.success('Đã gửi ký — bấm Ký số trong khung Luồng ký'), onError)
                }
              >
                Ký và gửi
              </Button>
            )}
            {p.canCancel && (
              <Popconfirm
                title="Hủy phiếu đề xuất?"
                onConfirm={() => cancel.mutateAsync().then(() => message.success('Đã hủy phiếu'), onError)}
              >
                <Button danger icon={<StopOutlined />}>
                  Hủy phiếu
                </Button>
              </Popconfirm>
            )}
          </>
        }
      />
      <Row gutter={[16, 16]}>
        <Col xs={24} xl={workflow.data ? 15 : 24}>
          <Card style={{ marginBottom: 16 }}>
            <Descriptions size="small" column={{ xs: 1, md: 3 }}>
              <Descriptions.Item label="Khoa/phòng">{p.departmentName}</Descriptions.Item>
              <Descriptions.Item label="Mảng">{domainLabel(p.domain)}</Descriptions.Item>
              <Descriptions.Item label="Kho">{p.warehouseName}</Descriptions.Item>
              <Descriptions.Item label="Kỳ">{p.periodText}</Descriptions.Item>
              <Descriptions.Item label="Người lập">{p.createdByName}</Descriptions.Item>
              <Descriptions.Item label="Ghi chú">{p.note ?? '—'}</Descriptions.Item>
            </Descriptions>
          </Card>
          <Card>
            <Tabs
              items={[
                {
                  key: 'lines',
                  label: `Mặt hàng (${p.lines.length})`,
                  children: (
                    <Table<ProposalLineDto>
                      rowKey="id"
                      size="small"
                      pagination={false}
                      dataSource={p.lines}
                      scroll={{ x: 'max-content' }}
                      columns={[
                        { title: 'Số HĐ', dataIndex: 'contractNumber' },
                        { title: 'Tên hàng', dataIndex: 'itemName' },
                        { title: 'ĐVT', dataIndex: 'unit' },
                        {
                          title: 'Tồn đầu kỳ',
                          dataIndex: 'openingStock',
                          align: 'right',
                          render: (v: number) => formatQuantity(v),
                        },
                        {
                          title: 'Nhập',
                          dataIndex: 'received',
                          align: 'right',
                          render: (v: number) => formatQuantity(v),
                        },
                        {
                          title: 'Xuất',
                          dataIndex: 'issued',
                          align: 'right',
                          render: (v: number) => formatQuantity(v),
                        },
                        {
                          title: 'Tồn',
                          dataIndex: 'currentStock',
                          align: 'right',
                          render: (v: number) => formatQuantity(v),
                        },
                        {
                          title: 'Còn lại HĐ',
                          dataIndex: 'contractRemaining',
                          align: 'right',
                          render: (v: number) => formatQuantity(v),
                        },
                        {
                          title: 'SL đề xuất',
                          dataIndex: 'proposedQuantity',
                          align: 'right',
                          render: (v: number) => <b>{formatQuantity(v)}</b>,
                        },
                        { title: 'Ghi chú', dataIndex: 'note' },
                      ]}
                    />
                  ),
                },
                {
                  key: 'pdf',
                  label: 'Phiếu (PDF)',
                  children: p.documentId ? <DocumentPdf documentId={p.documentId} /> : <Empty />,
                },
              ]}
            />
          </Card>
        </Col>
        {workflow.data && (
          <Col xs={24} xl={9}>
            <WorkflowCard wf={workflow.data} />
          </Col>
        )}
      </Row>
    </>
  );
}
