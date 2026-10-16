import {
  Alert,
  App,
  Button,
  Card,
  Col,
  Descriptions,
  Empty,
  Popconfirm,
  Row,
  Space,
  Table,
  Tabs,
  Typography,
} from 'antd';
import { DownloadOutlined, PhoneOutlined, SaveOutlined, SendOutlined, StopOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { downloadFile } from '../../api/http';
import {
  SUMMARIES,
  domainLabel,
  summaryApi,
  useProposalAction,
  useSummary,
  type SummaryLineDto,
} from '../../api/proposals';
import { useDocumentWorkflow } from '../../api/signing';
import { StatusTag } from '../../components/StatusTag';
import { useApiError } from '../../components/common';
import { DocHeader } from '../../components/layout';
import { QuantityInput } from '../../components/inputs';
import { formatDateTime, formatMoney, formatQuantity } from '../../utils/format';
import { WorkflowCard } from '../signing/WorkflowPanel';
import { DocumentPdf } from './ProposalDetailPage';

/** Chi tiết tổng hợp: bảng tổng hợp, cột SL duyệt (tổ trưởng sửa), gửi ký, xuất Excel, gọi hàng. */
export function SummaryDetailPage() {
  const { id } = useParams<{ id: string }>();
  const summary = useSummary(id);
  const s = summary.data;
  const workflow = useDocumentWorkflow(s?.documentId ?? undefined);
  const navigate = useNavigate();
  const { message } = App.useApp();
  const onError = useApiError();
  const [approved, setApproved] = useState<Record<string, number>>({});
  const submit = useProposalAction(() => summaryApi.submit(id!));
  const cancel = useProposalAction(() => summaryApi.cancel(id!));
  const saveApproved = useProposalAction(() =>
    summaryApi.setApproved(
      id!,
      Object.entries(approved).map(([lineId, approvedQuantity]) => ({ lineId, approvedQuantity })),
    ),
  );

  if (!s) return <Card loading={summary.isLoading} />;
  const changed = Object.keys(approved).length > 0;

  return (
    <>
      <DocHeader
        back={{ to: -1, label: 'Quay lại' }}
        plain
        code={
          <>
            Phiếu tổng hợp <span className="mono">{s.number}</span>
          </>
        }
        tags={<StatusTag domain="summaryStatus" value={s.status} />}
        actions={
          <>
            <Button
              icon={<DownloadOutlined />}
              onClick={() =>
                downloadFile(`${SUMMARIES}/${s.id}/export`, `tong-hop-${s.number}.xlsx`).catch(onError)
              }
            >
              Xuất Excel
            </Button>
            {s.canSubmit && (
              <Button
                type="primary"
                icon={<SendOutlined />}
                loading={submit.isPending}
                onClick={() =>
                  submit
                    .mutateAsync()
                    .then(() => message.success('Đã gửi ký duyệt — bấm Ký số trong khung Luồng ký'), onError)
                }
              >
                Gửi ký duyệt
              </Button>
            )}
            {s.canCall && (
              <Button
                type="primary"
                icon={<PhoneOutlined />}
                onClick={() => navigate(`/goi-hang/tao-moi?summary=${s.id}`)}
              >
                Gọi hàng
              </Button>
            )}
            {s.canCancel && (
              <Popconfirm
                title="Hủy phiếu tổng hợp?"
                description="Các phiếu đề xuất sẽ trở lại trạng thái chờ tổng hợp."
                onConfirm={() =>
                  cancel.mutateAsync().then(() => message.success('Đã hủy phiếu tổng hợp'), onError)
                }
              >
                <Button danger icon={<StopOutlined />}>
                  Hủy
                </Button>
              </Popconfirm>
            )}
          </>
        }
      />
      <Row gutter={[16, 16]}>
        <Col xs={24} xl={workflow.data ? 16 : 24}>
          <Card style={{ marginBottom: 16 }}>
            <Descriptions size="small" column={{ xs: 1, md: 3 }}>
              <Descriptions.Item label="Mảng">{domainLabel(s.domain)}</Descriptions.Item>
              <Descriptions.Item label="Kỳ">{s.periodText}</Descriptions.Item>
              <Descriptions.Item label="Người tổng hợp">{s.aggregatorName}</Descriptions.Item>
              <Descriptions.Item label="Phiếu đề xuất" span={2}>
                <Space wrap>
                  {s.proposals.map((p) => (
                    <a key={p.id} onClick={() => navigate(`/de-xuat/${p.id}`)}>
                      {p.number} ({p.departmentName})
                    </a>
                  ))}
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label="Duyệt lúc">{formatDateTime(s.approvedAt) || '—'}</Descriptions.Item>
            </Descriptions>
          </Card>
          {s.canEditApproved && (
            <Alert
              type="warning"
              showIcon
              style={{ marginBottom: 12 }}
              message="Tổ trưởng có thể sửa cột SL duyệt. Khi lưu, phiếu tạo phiên bản mới và người tổng hợp ký lại trước khi chuyển tiếp."
              action={
                <Button
                  type="primary"
                  size="small"
                  icon={<SaveOutlined />}
                  disabled={!changed}
                  loading={saveApproved.isPending}
                  onClick={() =>
                    saveApproved.mutateAsync().then(() => {
                      setApproved({});
                      message.success('Đã lưu SL duyệt — chuyển người tổng hợp ký lại');
                    }, onError)
                  }
                >
                  Lưu SL duyệt
                </Button>
              }
            />
          )}
          <Card>
            <Tabs
              items={[
                {
                  key: 'lines',
                  label: `Bảng tổng hợp (${s.lines.length})`,
                  children: (
                    <Table<SummaryLineDto>
                      rowKey="id"
                      size="small"
                      pagination={false}
                      dataSource={s.lines}
                      scroll={{ x: 'max-content' }}
                      summary={(rows) => (
                        <Table.Summary.Row>
                          <Table.Summary.Cell index={0} colSpan={12}>
                            <Typography.Text strong>Tổng giá trị duyệt</Typography.Text>
                          </Table.Summary.Cell>
                          <Table.Summary.Cell index={1} align="right">
                            <Typography.Text strong>
                              {formatMoney(rows.reduce((t, r) => t + r.amount, 0))}
                            </Typography.Text>
                          </Table.Summary.Cell>
                        </Table.Summary.Row>
                      )}
                      columns={[
                        { title: 'Nhà cung cấp', dataIndex: 'supplierName' },
                        { title: 'Số HĐ', dataIndex: 'contractNumber' },
                        { title: 'Tên hàng', dataIndex: 'itemName' },
                        { title: 'ĐVT', dataIndex: 'unit' },
                        {
                          title: 'SL đề xuất',
                          dataIndex: 'proposedQuantity',
                          align: 'right',
                          render: (v: number) => formatQuantity(v),
                        },
                        {
                          title: 'Tồn đầu',
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
                          title: 'SL duyệt',
                          key: 'approved',
                          align: 'right',
                          width: 150,
                          render: (_, r) =>
                            s.canEditApproved ? (
                              <QuantityInput
                                size="small"
                                max={r.contractRemaining}
                                value={approved[r.id] ?? r.approvedQuantity}
                                onChange={(v) => setApproved({ ...approved, [r.id]: v ?? 0 })}
                              />
                            ) : (
                              <b>{formatQuantity(r.approvedQuantity)}</b>
                            ),
                        },
                        {
                          title: 'Đã gọi',
                          dataIndex: 'calledQuantity',
                          align: 'right',
                          render: (v: number) => formatQuantity(v),
                        },
                        {
                          title: 'Còn được gọi',
                          dataIndex: 'remainingToCall',
                          align: 'right',
                          render: (v: number) => formatQuantity(v),
                        },
                        {
                          title: 'Thành tiền',
                          dataIndex: 'amount',
                          align: 'right',
                          render: (v: number) => formatMoney(v),
                        },
                      ]}
                    />
                  ),
                },
                {
                  key: 'pdf',
                  label: 'Phiếu (PDF)',
                  children: s.documentId ? <DocumentPdf documentId={s.documentId} /> : <Empty />,
                },
              ]}
            />
          </Card>
        </Col>
        {workflow.data && (
          <Col xs={24} xl={8}>
            <WorkflowCard wf={workflow.data} />
          </Col>
        )}
      </Row>
    </>
  );
}
