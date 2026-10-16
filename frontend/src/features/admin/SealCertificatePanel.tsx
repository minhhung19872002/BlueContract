import { App, Button, Form, Input, Popconfirm, Spin, Upload } from 'antd';
import { FileKey2, ShieldCheck, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { fetchSealCertificate, installSealCertificate, removeSealCertificate } from '../../api/auth';
import { useApiError } from '../../components/common';
import { Banner, InfoGrid, Panel } from '../../components/layout';
import { formatDate } from '../../utils/format';

const KEY = ['seal-certificate'];

/** "CN=Bệnh viện A, O=..., C=VN" → "Bệnh viện A". */
const commonName = (subject: string) => /(?:^|,\s*)CN=([^,]+)/.exec(subject)?.[1] ?? subject;

/**
 * Chứng thư số tổ chức (spec §8 "Đóng dấu tổ chức"): the hospital certificate the clerk's e-seal is signed with.
 * Without it, the seal uses the generated test certificate.
 */
export function SealCertificatePanel({ canManage }: { canManage: boolean }) {
  const client = useQueryClient();
  const { message } = App.useApp();
  const onError = useApiError();
  const [file, setFile] = useState<File | null>(null);
  const [form] = Form.useForm<{ password: string }>();
  const installed = useQuery({
    queryKey: KEY,
    // Days left are computed when the data arrives (render stays pure).
    queryFn: async () => {
      const cert = await fetchSealCertificate();
      return (
        cert && {
          ...cert,
          daysLeft: Math.floor((new Date(cert.notAfter).getTime() - Date.now()) / 86_400_000),
        }
      );
    },
  });

  const install = useMutation({
    mutationFn: (password: string) => installSealCertificate(file!, password),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: KEY });
      setFile(null);
      form.resetFields();
      message.success('Đã cài chứng thư số tổ chức');
    },
    onError,
  });
  const remove = useMutation({
    mutationFn: removeSealCertificate,
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: KEY });
      message.success('Đã gỡ chứng thư số tổ chức');
    },
    onError,
  });

  const c = installed.data;
  const daysLeft = c?.daysLeft ?? 0;

  return (
    <Panel
      title="Chứng thư số tổ chức (đóng dấu điện tử)"
      extra={
        c &&
        canManage && (
          <Popconfirm
            title="Gỡ chứng thư số tổ chức?"
            description="Các lần đóng dấu sau sẽ dùng chứng thư thử nghiệm cho tới khi cài chứng thư mới."
            onConfirm={() => remove.mutate()}
            okText="Gỡ"
            cancelText="Hủy"
          >
            <Button size="small" danger icon={<Trash2 size={14} aria-hidden />} loading={remove.isPending}>
              Gỡ chứng thư
            </Button>
          </Popconfirm>
        )
      }
    >
      {installed.isLoading ? (
        <Spin />
      ) : c ? (
        <div className="seal-cert">
          <Banner
            tone={c.isSelfSigned ? 'warning' : daysLeft < 30 ? 'warning' : 'success'}
            title={
              c.isSelfSigned
                ? 'Chứng thư tự cấp — chỉ dùng thử nghiệm, chữ ký không có giá trị pháp lý.'
                : daysLeft < 30
                  ? `Chứng thư sẽ hết hạn sau ${daysLeft} ngày — gia hạn và cài lại trước ngày ${formatDate(c.notAfter)}.`
                  : 'Văn thư đóng dấu điện tử bằng chứng thư này.'
            }
          />
          <InfoGrid
            minColumn={220}
            items={[
              { label: 'Tổ chức', value: commonName(c.subject) },
              { label: 'Tổ chức cấp (CA)', value: commonName(c.issuer) },
              { label: 'Số hiệu (serial)', value: c.serialNumber, mono: true },
              { label: 'Hiệu lực', value: `${formatDate(c.notBefore)} – ${formatDate(c.notAfter)}` },
            ]}
          />
        </div>
      ) : (
        <Banner
          tone="info"
          title="Chưa cài chứng thư số tổ chức — đóng dấu điện tử đang dùng chứng thư thử nghiệm do hệ thống tạo."
        />
      )}
      {canManage && (
        <Form
          form={form}
          layout="vertical"
          className="seal-cert__form"
          onFinish={(v) => install.mutate(v.password)}
        >
          <Form.Item label={c ? 'Thay chứng thư (.p12 / .pfx)' : 'Tệp chứng thư (.p12 / .pfx)'} required>
            <Upload
              accept=".p12,.pfx,application/x-pkcs12"
              maxCount={1}
              fileList={file ? [{ uid: 'cert', name: file.name, status: 'done' }] : []}
              onRemove={() => setFile(null)}
              beforeUpload={(f) => {
                setFile(f);
                return false;
              }}
            >
              <Button icon={<FileKey2 size={16} aria-hidden />}>Chọn tệp chứng thư</Button>
            </Upload>
          </Form.Item>
          <Form.Item
            name="password"
            label="Mật khẩu tệp chứng thư"
            rules={[{ required: true, message: 'Nhập mật khẩu của tệp chứng thư' }]}
            extra="Mật khẩu chỉ dùng để mở tệp; khóa bí mật được lưu mã hóa trên máy chủ."
          >
            <Input.Password autoComplete="off" />
          </Form.Item>
          <Button
            type="primary"
            htmlType="submit"
            icon={<ShieldCheck size={16} aria-hidden />}
            disabled={!file}
            loading={install.isPending}
          >
            Cài chứng thư
          </Button>
        </Form>
      )}
    </Panel>
  );
}
