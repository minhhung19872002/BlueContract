import { App, Button, Popconfirm, Spin, Upload } from 'antd';
import { FileKey2, Trash2 } from 'lucide-react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { fetchMyCertificate, registerMyCertificate, removeMyCertificate } from '../../api/auth';
import { useAuth } from '../../auth/useAuth';
import { useApiError } from '../../components/common';
import { Banner, InfoGrid } from '../../components/layout';
import { formatDate } from '../../utils/format';

const KEY = ['my-certificate'];

/** "CN=Nguyễn Văn A, O=..., C=VN" → "Nguyễn Văn A". */
const commonName = (dn: string) => /(?:^|,\s*)CN=([^,]+)/.exec(dn)?.[1] ?? dn;

/**
 * Đăng ký chứng thư số (spec §9.2-7): the public certificate of the token / remote-signing account the user signs with.
 * Signing with a USB token then accepts only this certificate.
 */
export function MyCertificateCard() {
  const { user } = useAuth();
  const client = useQueryClient();
  const { message } = App.useApp();
  const onError = useApiError();
  const current = useQuery({ queryKey: KEY, queryFn: fetchMyCertificate });
  const register = useMutation({
    mutationFn: registerMyCertificate,
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: KEY });
      message.success('Đã đăng ký chứng thư số');
    },
    onError,
  });
  const remove = useMutation({
    mutationFn: removeMyCertificate,
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: KEY });
      message.success('Đã hủy đăng ký chứng thư số');
    },
    onError,
  });
  const c = current.data;

  return (
    <section className="account__card">
      <div>
        <h2 className="account__title">Chứng thư số dùng để ký</h2>
        <p className="account__lead">
          Tải lên tệp chứng thư công khai (.cer, .crt hoặc .pem) của USB token hoặc tài khoản ký số từ xa. Khi
          ký bằng USB token, hệ thống chỉ chấp nhận chứng thư đã đăng ký.
          {user?.supplierName && ' Chứng thư tổ chức phải mang mã số thuế của công ty.'}
        </p>
      </div>
      {current.isLoading ? (
        <Spin />
      ) : c?.serial ? (
        <>
          {c.isExpired ? (
            <Banner
              tone="danger"
              title={`Chứng thư đã hết hạn ngày ${formatDate(c.validTo)} — hãy đăng ký chứng thư mới.`}
            />
          ) : c.expiresSoon ? (
            <Banner
              tone="warning"
              title={`Chứng thư sắp hết hạn (${formatDate(c.validTo)}) — gia hạn và đăng ký lại.`}
            />
          ) : null}
          <InfoGrid
            minColumn={200}
            items={[
              { label: 'Chủ thể', value: c.subject ? commonName(c.subject) : '—' },
              { label: 'Tổ chức cấp (CA)', value: c.issuer ? commonName(c.issuer) : '—' },
              { label: 'Số hiệu (serial)', value: c.serial, mono: true },
              { label: 'Hiệu lực đến', value: formatDate(c.validTo) },
            ]}
          />
        </>
      ) : (
        <Banner tone="info" title="Chưa đăng ký chứng thư số." />
      )}
      <div className="account__cert-actions">
        <Upload
          accept=".cer,.crt,.pem,.der"
          showUploadList={false}
          beforeUpload={(file) => {
            register.mutate(file);
            return false;
          }}
        >
          <Button icon={<FileKey2 size={16} aria-hidden />} loading={register.isPending}>
            {c?.serial ? 'Đăng ký chứng thư khác' : 'Tải lên chứng thư'}
          </Button>
        </Upload>
        {c?.serial && (
          <Popconfirm
            title="Hủy đăng ký chứng thư số?"
            description="Sau khi hủy, hệ thống không còn đối chiếu chứng thư khi bạn ký bằng USB token."
            okText="Hủy đăng ký"
            cancelText="Đóng"
            onConfirm={() => remove.mutate()}
          >
            <Button danger icon={<Trash2 size={14} aria-hidden />} loading={remove.isPending}>
              Hủy đăng ký
            </Button>
          </Popconfirm>
        )}
      </div>
    </section>
  );
}
