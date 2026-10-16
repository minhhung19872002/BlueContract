import { App, Button, Modal, Space, Table, Tag, Typography, Upload } from 'antd';
import { DownloadOutlined, InboxOutlined } from '@ant-design/icons';
import { useState } from 'react';
import {
  bidApi,
  downloadImportTemplate,
  useBidAction,
  type ImportPreviewDto,
  type ImportRowDto,
  type ItemImportKind,
} from '../../api/procurement';
import { useApiError } from '../../components/common';
import { Banner, FormSteps, InfoGrid } from '../../components/layout';
import { formatMoney, formatQuantity } from '../../utils/format';

const COLUMN_LABELS: Record<string, string> = {
  HisKey: 'Mã key HIS',
  Code: 'Mã hàng',
  Name: 'Tên hàng',
  Unit: 'ĐVT',
  Category: 'Hạng mục',
  SupplierTaxCode: 'MST NCC',
  Quantity: 'Số lượng',
  UnitPrice: 'Đơn giá',
  VatRate: 'Thuế suất',
};

/** Cách B (spec §6.1 B1): upload → preview with error rows highlighted → import only when the file is clean. */
export function ImportItemsDialog({
  bidId,
  kind,
  open,
  onClose,
}: {
  bidId: string;
  kind: ItemImportKind;
  open: boolean;
  onClose: () => void;
}) {
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<ImportPreviewDto | null>(null);
  const [checking, setChecking] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const commit = useBidAction((f: File) => bidApi.commitImport(bidId, f));
  const onError = useApiError();
  const { message } = App.useApp();

  const close = () => {
    setFile(null);
    setPreview(null);
    setConfirming(false);
    onClose();
  };
  const step = !preview ? 0 : confirming ? 2 : 1;
  const updates = preview?.rows.filter((r) => !r.errors.length && r.isUpdate).length ?? 0;
  const runImport = async () => {
    try {
      const result = await commit.mutateAsync(file!);
      message.success(`Đã import: ${result.created} mặt hàng mới, ${result.updated} mặt hàng cập nhật`);
      close();
    } catch (e) {
      onError(e);
    }
  };

  const hasError = (r: ImportRowDto, column: string) => r.errors.some((e) => e.column === column);
  const cell =
    (column: string, render?: (r: ImportRowDto) => React.ReactNode) => (_: unknown, r: ImportRowDto) => (
      <span style={hasError(r, column) ? { color: 'var(--bp-danger-fg)', fontWeight: 600 } : undefined}>
        {render
          ? render(r)
          : (r[(column.charAt(0).toLowerCase() + column.slice(1)) as keyof ImportRowDto] as string) || '—'}
      </span>
    );

  return (
    <Modal
      title="Nhập danh mục hàng hóa từ Excel"
      open={open}
      onCancel={close}
      width="min(1200px, 96vw)"
      destroyOnHidden
      footer={
        <Space wrap>
          <Button onClick={close}>Đóng</Button>
          {step === 1 && (
            <Button
              onClick={() => {
                setFile(null);
                setPreview(null);
              }}
            >
              Chọn tệp khác
            </Button>
          )}
          {step === 1 && (
            <Button type="primary" disabled={preview!.errorRows > 0} onClick={() => setConfirming(true)}>
              Tiếp tục
            </Button>
          )}
          {step === 2 && <Button onClick={() => setConfirming(false)}>Quay lại</Button>}
          {step === 2 && (
            <Button type="primary" loading={commit.isPending} onClick={runImport}>
              Nhập {preview!.validRows} dòng
            </Button>
          )}
        </Space>
      }
    >
      <FormSteps
        steps={['Tải tệp Excel', 'Kiểm tra dữ liệu', 'Xác nhận import']}
        current={step}
        onChange={(i) => {
          if (i === 0) {
            setFile(null);
            setPreview(null);
          }
          setConfirming(false);
        }}
      />
      {step === 0 && (
        <Space direction="vertical" style={{ width: '100%' }}>
          <Button icon={<DownloadOutlined />} onClick={() => downloadImportTemplate(kind).catch(onError)}>
            Tải biểu mẫu{' '}
            {kind === 'Medical' ? 'thuốc, VTYT, hóa chất, sinh phẩm' : 'hàng hóa, tài sản, dịch vụ'}
          </Button>
          <Upload.Dragger
            accept=".xlsx"
            showUploadList={false}
            disabled={checking}
            beforeUpload={(f) => {
              setFile(f);
              setChecking(true);
              bidApi
                .previewImport(bidId, f)
                .then(setPreview)
                .catch((e) => {
                  setPreview(null);
                  onError(e);
                })
                .finally(() => setChecking(false));
              return false;
            }}
          >
            <p className="ant-upload-drag-icon">
              <InboxOutlined />
            </p>
            <p className="ant-upload-text">
              {checking ? 'Đang kiểm tra tệp...' : file ? file.name : 'Kéo thả hoặc bấm để chọn tệp .xlsx'}
            </p>
          </Upload.Dragger>
        </Space>
      )}
      {step === 1 && preview && (
        <Space direction="vertical" style={{ width: '100%' }}>
          <Banner
            tone={preview.errorRows ? 'danger' : 'success'}
            title={
              preview.errorRows
                ? `${preview.errorRows}/${preview.totalRows} dòng có lỗi — sửa tệp và tải lại để import.`
                : `Tệp hợp lệ: ${preview.validRows} dòng sẵn sàng import.`
            }
          />
          <Table<ImportRowDto>
            rowKey="rowNumber"
            size="small"
            dataSource={preview.rows}
            pagination={{ pageSize: 50, hideOnSinglePage: true }}
            scroll={{ x: 'max-content', y: 420 }}
            rowClassName={(r) => (r.errors.length ? 'import-row-error' : '')}
            onRow={(r) => ({ style: r.errors.length ? { background: 'var(--bp-danger-bg)' } : undefined })}
            columns={[
              { title: 'Dòng', dataIndex: 'rowNumber', width: 60 },
              { title: 'Mã key HIS', key: 'hisKey', render: cell('HisKey') },
              { title: 'Mã hàng', key: 'code', render: cell('Code') },
              { title: 'Tên hàng', key: 'name', render: cell('Name') },
              { title: 'ĐVT', key: 'unit', render: cell('Unit') },
              {
                title: 'Hạng mục',
                key: 'category',
                render: cell('Category', (r) => r.categoryCode ?? '—'),
              },
              {
                title: 'NCC',
                key: 'supplier',
                render: cell('SupplierTaxCode', (r) =>
                  r.supplierName ? `${r.supplierTaxCode} — ${r.supplierName}` : (r.supplierTaxCode ?? '—'),
                ),
              },
              {
                title: 'Số lượng',
                key: 'qty',
                align: 'right',
                render: cell('Quantity', (r) => formatQuantity(r.quantity)),
              },
              {
                title: 'Đơn giá',
                key: 'price',
                align: 'right',
                render: cell('UnitPrice', (r) => formatMoney(r.unitPrice)),
              },
              {
                title: 'Kết quả',
                key: 'result',
                render: (_, r) =>
                  r.errors.length ? (
                    <Space direction="vertical" size={0}>
                      {r.errors.map((e, i) => (
                        <Typography.Text key={i} type="danger" style={{ fontSize: 12 }}>
                          {COLUMN_LABELS[e.column] ? `${COLUMN_LABELS[e.column]}: ` : ''}
                          {e.message}
                        </Typography.Text>
                      ))}
                    </Space>
                  ) : (
                    <Tag color={r.isUpdate ? 'blue' : 'green'}>{r.isUpdate ? 'Cập nhật' : 'Thêm mới'}</Tag>
                  ),
              },
            ]}
          />
        </Space>
      )}
      {step === 2 && preview && (
        <Space direction="vertical" style={{ width: '100%' }}>
          <Banner
            tone="info"
            title="Kiểm tra lần cuối rồi bấm Nhập. Mặt hàng đã có (cùng mã key HIS) sẽ được cập nhật."
          />
          <InfoGrid
            items={[
              { label: 'Tệp', value: file?.name, mono: true },
              { label: 'Số dòng hợp lệ', value: preview.validRows },
              { label: 'Mặt hàng mới', value: preview.validRows - updates },
              { label: 'Mặt hàng cập nhật', value: updates },
            ]}
          />
        </Space>
      )}
    </Modal>
  );
}
