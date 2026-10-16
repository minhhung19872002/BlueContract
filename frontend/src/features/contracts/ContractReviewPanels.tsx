import { Alert, App, Button, Card, Empty, Radio, Space, Table, Tag, Typography, Upload } from 'antd';
import {
  CheckOutlined,
  EditOutlined,
  RollbackOutlined,
  SendOutlined,
  UploadOutlined,
} from '@ant-design/icons';
import { useState } from 'react';
import {
  contractApi,
  useContractAction,
  useContractRevisions,
  type ContractDto,
  type DocxRevision,
} from '../../api/procurement';
import { useApproveStep, useReturnStep } from '../../api/signing';
import { StatusTag } from '../../components/StatusTag';
import { ReasonDialog } from '../../components/ReasonDialog';
import { useApiError } from '../../components/common';
import { formatDateTime } from '../../utils/format';

function RevisionTable({
  revisions,
  decisions,
  onDecide,
}: {
  revisions: DocxRevision[];
  decisions?: Record<string, boolean>;
  onDecide?: (id: string, accept: boolean) => void;
}) {
  return (
    <Table<DocxRevision>
      rowKey="id"
      size="small"
      pagination={false}
      dataSource={revisions}
      scroll={{ x: 'max-content', y: 420 }}
      locale={{ emptyText: <Empty description="Không còn thay đổi nào cần xử lý" /> }}
      columns={[
        { title: '#', dataIndex: 'id', width: 50 },
        {
          title: 'Loại',
          dataIndex: 'kind',
          width: 90,
          render: (v: string) => <StatusTag domain="revisionKind" value={v} />,
        },
        {
          title: 'Nội dung thay đổi',
          key: 'text',
          render: (_, r) => (
            <Space direction="vertical" size={0} style={{ maxWidth: 520 }}>
              <Typography.Text
                delete={r.kind === 'Deletion'}
                style={{
                  color: r.kind === 'Insertion' ? '#cf1322' : r.kind === 'Deletion' ? '#8c8c8c' : undefined,
                }}
              >
                {r.text || '(trống)'}
              </Typography.Text>
              {r.context && (
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  Đoạn: {r.context}
                </Typography.Text>
              )}
            </Space>
          ),
        },
        { title: 'Người sửa', dataIndex: 'author', width: 140 },
        {
          title: 'Thời điểm',
          dataIndex: 'date',
          width: 140,
          render: (v: string | null) => formatDateTime(v),
        },
        ...(onDecide
          ? [
              {
                title: 'Quyết định',
                key: 'decision',
                width: 210,
                render: (_: unknown, r: DocxRevision) => (
                  <Radio.Group
                    size="small"
                    value={decisions?.[r.id]}
                    onChange={(e) => onDecide(r.id, e.target.value as boolean)}
                    options={[
                      { value: true, label: 'Chấp nhận' },
                      { value: false, label: 'Từ chối' },
                    ]}
                    optionType="button"
                  />
                ),
              },
            ]
          : []),
      ]}
    />
  );
}

/** NCC rà soát hợp đồng (menu Hợp đồng của Cổng NCC, spec §6.1 B3-3). */
export function SupplierReviewPanel({
  contract,
  onOpenEditor,
}: {
  contract: ContractDto;
  onOpenEditor: () => void;
}) {
  const revisions = useContractRevisions(contract.id, true);
  const respond = useContractAction((v: { agree: boolean; comment?: string }) =>
    contractApi.supplierResponse(contract.id, v.agree, v.comment),
  );
  const upload = useContractAction((file: File) => contractApi.supplierRevision(contract.id, file));
  const { message } = App.useApp();
  const onError = useApiError();
  const [sending, setSending] = useState(false);
  const pending = revisions.data?.revisions.length ?? contract.actions.pendingRevisionCount;

  return (
    <Card
      style={{ marginBottom: 16, borderColor: '#faad14' }}
      title={
        <Space wrap>
          <Tag color="gold">Chờ Quý công ty rà soát</Tag>
          {pending > 0 ? `${pending} đề xuất sửa đổi đang theo dõi` : 'Chưa có sửa đổi'}
        </Space>
      }
    >
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 12 }}
        message="Mở hợp đồng ở chế độ Rà soát (bắt buộc theo dõi thay đổi): nội dung xóa hiển thị gạch ngang, nội dung thêm bôi đỏ. Không sửa: chọn Đồng ý ký. Có sửa: chọn Gửi lại để Kế toán xem xét."
      />
      <Space wrap style={{ marginBottom: 12 }}>
        <Button icon={<EditOutlined />} onClick={onOpenEditor}>
          Mở rà soát trực tuyến
        </Button>
        <Upload
          accept=".docx"
          showUploadList={false}
          beforeUpload={(f) => {
            upload.mutateAsync(f).then(() => message.success('Đã tải lên bản đề xuất sửa đổi'), onError);
            return false;
          }}
        >
          <Button icon={<UploadOutlined />} loading={upload.isPending}>
            Tải lên bản đã sửa (Track Changes)
          </Button>
        </Upload>
        <Button
          type="primary"
          icon={<CheckOutlined />}
          disabled={pending > 0}
          loading={respond.isPending && respond.variables?.agree}
          onClick={() =>
            respond
              .mutateAsync({ agree: true })
              .then(() => message.success('Đã đồng ý — hợp đồng chuyển sang bước ký số'), onError)
          }
        >
          Đồng ý ký
        </Button>
        <Button icon={<SendOutlined />} onClick={() => setSending(true)}>
          Gửi lại (có sửa đổi)
        </Button>
      </Space>
      {pending > 0 && <RevisionTable revisions={revisions.data?.revisions ?? []} />}

      <ReasonDialog
        open={sending}
        title="Gửi lại hợp đồng cho Bệnh viện"
        okText="Gửi lại"
        danger={false}
        reasonLabel="Ý kiến gửi kèm"
        placeholder="Ý kiến về các sửa đổi đề xuất…"
        required={pending === 0}
        requiredMessage="Chưa có sửa đổi — vui lòng nhập ý kiến"
        loading={respond.isPending}
        onClose={() => setSending(false)}
        onConfirm={async (comment) => {
          try {
            await respond.mutateAsync({ agree: false, comment: comment || undefined });
            message.success('Đã gửi lại — Kế toán sẽ xem xét các đề xuất sửa đổi');
          } catch (e) {
            onError(e);
            throw e;
          }
        }}
      />
    </Card>
  );
}

/** Kế toán xem thay đổi của NCC: chấp nhận / từ chối từng thay đổi, gửi lại NCC hoặc chấp nhận để chuyển ký (spec §6.1 B3-4). */
export function AccountantReviewPanel({
  contract,
  onOpenEditor,
}: {
  contract: ContractDto;
  onOpenEditor: () => void;
}) {
  const revisions = useContractRevisions(contract.id, true);
  const [decisions, setDecisions] = useState<Record<string, boolean>>({});
  const resolve = useContractAction((v: { versionId: string; decisions: Record<string, boolean> }) =>
    contractApi.resolveRevisions(
      contract.id,
      v.versionId,
      Object.entries(v.decisions).map(([id, accept]) => ({ id, accept })),
    ),
  );
  const approve = useApproveStep();
  const returnStep = useReturnStep();
  const { message, modal } = App.useApp();
  const onError = useApiError();
  const [returning, setReturning] = useState(false);
  const list = revisions.data?.revisions ?? [];
  const stepId = contract.actions.accountantReviewStepId!;

  return (
    <Card
      style={{ marginBottom: 16, borderColor: '#fa8c16' }}
      title={
        <Space>
          Thay đổi do nhà cung cấp đề xuất <Tag color="orange">{list.length}</Tag>
        </Space>
      }
    >
      <Space wrap style={{ marginBottom: 12 }}>
        <Button onClick={() => setDecisions(Object.fromEntries(list.map((r) => [r.id, true])))}>
          Chọn chấp nhận tất cả
        </Button>
        <Button onClick={() => setDecisions(Object.fromEntries(list.map((r) => [r.id, false])))}>
          Chọn từ chối tất cả
        </Button>
        <Button
          type="primary"
          disabled={Object.keys(decisions).length === 0 || !revisions.data}
          loading={resolve.isPending}
          onClick={() =>
            resolve.mutateAsync({ versionId: revisions.data!.versionId, decisions }).then(() => {
              setDecisions({});
              message.success('Đã áp dụng quyết định — tạo phiên bản mới');
            }, onError)
          }
        >
          Áp dụng ({Object.keys(decisions).length})
        </Button>
        <Button icon={<EditOutlined />} onClick={onOpenEditor}>
          Xem / sửa trong trình soạn thảo
        </Button>
      </Space>
      <RevisionTable
        revisions={list}
        decisions={decisions}
        onDecide={(id, accept) => setDecisions({ ...decisions, [id]: accept })}
      />
      <Space wrap style={{ marginTop: 12 }}>
        <Button icon={<RollbackOutlined />} onClick={() => setReturning(true)}>
          Gửi lại NCC xác nhận
        </Button>
        <Button
          type="primary"
          icon={<CheckOutlined />}
          loading={approve.isPending}
          onClick={() =>
            modal.confirm({
              title: 'Chấp nhận bản hiện tại và chuyển NCC ký số?',
              content: list.length
                ? `Còn ${list.length} thay đổi chưa xử lý — hệ thống sẽ chấp nhận toàn bộ khi chốt bản.`
                : 'Hệ thống chốt bản, chuyển PDF và chuyển sang bước NCC ký số.',
              onOk: () =>
                approve
                  .mutateAsync({ stepId, comment: 'Kế toán chấp nhận nội dung sửa đổi của NCC' })
                  .then(() => message.success('Đã chốt bản — chuyển NCC ký số'), onError),
            })
          }
        >
          Chấp nhận & chuyển ký
        </Button>
      </Space>

      <ReasonDialog
        open={returning}
        title="Gửi lại nhà cung cấp"
        okText="Gửi lại NCC"
        danger={false}
        reasonLabel="Ý kiến của Kế toán"
        placeholder="VD: Chấp nhận thay đổi thời gian giao hàng, không chấp nhận giảm hạn dùng tối thiểu."
        loading={returnStep.isPending}
        onClose={() => setReturning(false)}
        onConfirm={async (reason) => {
          try {
            await returnStep.mutateAsync({ stepId, toOrder: contract.actions.supplierReviewOrder!, reason });
            message.success('Đã gửi lại NCC');
          } catch (e) {
            onError(e);
            throw e;
          }
        }}
      />
    </Card>
  );
}
