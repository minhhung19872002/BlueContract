import { Alert, App, Card, Result, Space, Table, Tag, Tooltip, Typography, Upload } from 'antd';
import { CheckCircleOutlined, CloseCircleOutlined, InboxOutlined, WarningOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { verifyUpload, type PdfSignatureInfo, type VerifyResultDto } from '../../api/signing';
import { toApiProblem } from '../../api/http';
import { PageHeader } from '../../components/common';
import { formatDateTime } from '../../utils/format';

/** Signature list with integrity, signer, time and certificate details (shared with the document page). */
export function SignatureTable({ result }: { result: VerifyResultDto }) {
  return (
    <Table<PdfSignatureInfo>
      rowKey="fieldName"
      size="middle"
      pagination={false}
      dataSource={result.signatures}
      scroll={{ x: 'max-content' }}
      columns={[
        { title: '#', dataIndex: 'revision', width: 48 },
        {
          title: 'Toàn vẹn',
          key: 'integrity',
          render: (_, s) =>
            s.integrityValid ? (
              <Tag color="green" icon={<CheckCircleOutlined />}>
                Hợp lệ
              </Tag>
            ) : (
              <Tag color="red" icon={<CloseCircleOutlined />}>
                Đã bị sửa đổi
              </Tag>
            ),
        },
        {
          title: 'Người ký',
          key: 'signer',
          render: (_, s) => (
            <Space direction="vertical" size={0}>
              <Typography.Text strong>{s.signerName ?? s.signerSubject}</Typography.Text>
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                {s.signerSubject}
              </Typography.Text>
            </Space>
          ),
        },
        { title: 'Thời điểm ký', dataIndex: 'signingTime', render: (v: string | null) => formatDateTime(v) },
        {
          title: 'Chứng thư',
          key: 'cert',
          render: (_, s) => (
            <Space direction="vertical" size={0}>
              <Typography.Text style={{ fontSize: 12 }}>Cấp bởi: {s.issuerSubject}</Typography.Text>
              <Typography.Text style={{ fontSize: 12 }}>Serial: {s.serialNumber}</Typography.Text>
              <Space size={4}>
                {s.isSelfSigned && (
                  <Tooltip title="Chứng thư thử nghiệm tự cấp — không có giá trị pháp lý">
                    <Tag color="orange" icon={<WarningOutlined />}>
                      Tự cấp (thử nghiệm)
                    </Tag>
                  </Tooltip>
                )}
                {s.hasTimestamp && <Tag color="blue">Có dấu thời gian</Tag>}
              </Space>
            </Space>
          ),
        },
        {
          title: 'Phạm vi',
          key: 'coverage',
          render: (_, s) =>
            s.coversWholeDocument ? (
              <Tag color="green">Toàn bộ tài liệu</Tag>
            ) : (
              <Tooltip title="Tài liệu có thêm phần ký sau chữ ký này (chữ ký tiếp theo) — bình thường với luồng nhiều người ký.">
                <Tag>
                  Bản sửa đổi {s.revision}/{s.totalRevisions}
                </Tag>
              </Tooltip>
            ),
        },
      ]}
    />
  );
}

export function VerifySignaturePage() {
  const [result, setResult] = useState<{ name: string; data: VerifyResultDto } | null>(null);
  const [loading, setLoading] = useState(false);
  const { message } = App.useApp();

  return (
    <>
      <PageHeader
        title="Xác minh chữ ký số"
        subtitle="Tải lên tệp PDF để kiểm tra các chữ ký số và tính toàn vẹn"
      />
      <Card style={{ marginBottom: 16 }}>
        <Upload.Dragger
          accept=".pdf,application/pdf"
          showUploadList={false}
          disabled={loading}
          beforeUpload={(file) => {
            setLoading(true);
            verifyUpload(file)
              .then((data) => setResult({ name: file.name, data }))
              .catch((e) => message.error(toApiProblem(e).title))
              .finally(() => setLoading(false));
            return false;
          }}
        >
          <p className="ant-upload-drag-icon">
            <InboxOutlined />
          </p>
          <p className="ant-upload-text">
            {loading ? 'Đang xác minh...' : 'Kéo thả hoặc bấm để chọn tệp PDF'}
          </p>
          <p className="ant-upload-hint">Tệp chỉ dùng để kiểm tra, không được lưu lại trên hệ thống.</p>
        </Upload.Dragger>
      </Card>
      {result && (
        <Card title={result.name}>
          {result.data.signatureCount === 0 ? (
            <Result status="warning" title="Tệp không có chữ ký số" />
          ) : (
            <>
              <Alert
                showIcon
                style={{ marginBottom: 12 }}
                type={result.data.allValid ? 'success' : 'error'}
                message={
                  result.data.allValid
                    ? `Tất cả ${result.data.signatureCount} chữ ký đều toàn vẹn — nội dung không bị thay đổi sau khi ký.`
                    : 'Có chữ ký không hợp lệ: nội dung đã bị thay đổi sau khi ký.'
                }
              />
              <SignatureTable result={result.data} />
            </>
          )}
        </Card>
      )}
    </>
  );
}
