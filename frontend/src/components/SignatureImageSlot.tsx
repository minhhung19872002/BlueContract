import { App, Button, Popconfirm, Spin, Upload } from 'antd';
import { ImagePlus, Trash2 } from 'lucide-react';
import { useEffect, useMemo } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  fetchSignatureImage,
  removeSignatureImage,
  uploadSignatureImage,
  type SignatureImageSlot as Slot,
} from '../api/auth';
import { useApiError } from './common';
import './signatureImageSlot.css';

/**
 * One image drawn in the visible signature (spec §8): handwritten signature, initials or the organisation seal.
 * Shows the current image on a white "paper" tile, with replace and remove actions.
 */
export function SignatureImageSlot({
  slot,
  title,
  hint,
  readOnly,
}: {
  slot: Slot;
  title: string;
  hint: string;
  readOnly?: boolean;
}) {
  const key = ['signature-image', slot];
  const client = useQueryClient();
  const { message } = App.useApp();
  const onError = useApiError();
  const image = useQuery({ queryKey: key, queryFn: () => fetchSignatureImage(slot) });
  const url = useMemo(() => (image.data ? URL.createObjectURL(image.data) : null), [image.data]);
  useEffect(
    () => () => {
      if (url) URL.revokeObjectURL(url);
    },
    [url],
  );

  const upload = useMutation({
    mutationFn: (file: File) => uploadSignatureImage(slot, file),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: key });
      message.success(`Đã cập nhật ${title.toLowerCase()}`);
    },
    onError,
  });
  const remove = useMutation({
    mutationFn: () => removeSignatureImage(slot),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: key });
      message.success(`Đã xóa ${title.toLowerCase()}`);
    },
    onError,
  });

  return (
    <div className="sig-slot">
      <div className="sig-slot__paper" aria-label={title}>
        {image.isLoading ? (
          <Spin size="small" />
        ) : url ? (
          <img src={url} alt={title} />
        ) : (
          <span className="sig-slot__empty">Chưa có ảnh</span>
        )}
      </div>
      <div className="sig-slot__text">
        <span className="sig-slot__title">{title}</span>
        <span className="sig-slot__hint">{hint}</span>
        {!readOnly && (
          <span className="sig-slot__actions">
            <Upload
              accept="image/png,image/jpeg"
              showUploadList={false}
              beforeUpload={(file) => {
                upload.mutate(file);
                return false;
              }}
            >
              <Button size="small" icon={<ImagePlus size={14} aria-hidden />} loading={upload.isPending}>
                {url ? 'Thay ảnh' : 'Tải ảnh lên'}
              </Button>
            </Upload>
            {url && (
              <Popconfirm
                title={`Xóa ${title.toLowerCase()}?`}
                onConfirm={() => remove.mutate()}
                okText="Xóa"
                cancelText="Hủy"
              >
                <Button
                  size="small"
                  danger
                  icon={<Trash2 size={14} aria-hidden />}
                  loading={remove.isPending}
                >
                  Xóa
                </Button>
              </Popconfirm>
            )}
          </span>
        )}
      </div>
    </div>
  );
}
