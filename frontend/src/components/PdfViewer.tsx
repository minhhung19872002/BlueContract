import { Alert, Button, Empty, Space, Spin, Typography } from 'antd';
import { MinusOutlined, PlusOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { Document, Page, pdfjs } from 'react-pdf';
import 'react-pdf/dist/Page/TextLayer.css';
import 'react-pdf/dist/Page/AnnotationLayer.css';
import { toApiProblem } from '../api/http';

pdfjs.GlobalWorkerOptions.workerSrc = new URL(
  'pdfjs-dist/build/pdf.worker.min.mjs',
  import.meta.url,
).toString();

interface Props {
  /** Loads the PDF; re-run whenever `cacheKey` changes. */
  load: () => Promise<Blob>;
  cacheKey: string;
  height?: number | string;
}

/** In-app PDF viewer (pdf.js) that fits the container width — usable on tablets for signers (spec §2). */
export function PdfViewer({ load, cacheKey, height = '75vh' }: Props) {
  const file = useQuery({
    queryKey: ['pdf-viewer', cacheKey],
    queryFn: load,
    staleTime: 60_000,
    gcTime: 60_000,
  });
  const [renderError, setRenderError] = useState<string | null>(null);
  const [pages, setPages] = useState(0);
  const [zoom, setZoom] = useState(1);
  const [width, setWidth] = useState(800);
  const container = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const el = container.current;
    if (!el) return;
    const observer = new ResizeObserver(([entry]) =>
      setWidth(Math.max(280, (entry?.contentRect.width ?? 800) - 24)),
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const error = file.isError ? toApiProblem(file.error).title : renderError;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <Space>
        <Button
          size="small"
          icon={<MinusOutlined />}
          onClick={() => setZoom((z) => Math.max(0.5, z - 0.25))}
        />
        <Typography.Text>{Math.round(zoom * 100)}%</Typography.Text>
        <Button size="small" icon={<PlusOutlined />} onClick={() => setZoom((z) => Math.min(3, z + 0.25))} />
        {pages > 0 && <Typography.Text type="secondary">{pages} trang</Typography.Text>}
      </Space>
      <div
        ref={container}
        style={{
          height,
          overflow: 'auto',
          background: '#e9ecef',
          padding: 12,
          borderRadius: 6,
          textAlign: 'center',
        }}
      >
        {error && <Alert type="error" showIcon message={error} />}
        {!error && file.isPending && <Spin style={{ marginTop: 48 }} />}
        {file.data && (
          <Document
            file={file.data}
            onLoadSuccess={({ numPages }) => setPages(numPages)}
            onLoadError={() => setRenderError('Tệp PDF không hợp lệ.')}
            loading={<Spin />}
            noData={<Empty description="Không có dữ liệu" />}
          >
            {Array.from({ length: pages }, (_, i) => (
              <div
                key={i}
                style={{ display: 'inline-block', marginBottom: 12, boxShadow: '0 1px 4px rgba(0,0,0,.2)' }}
              >
                <Page pageNumber={i + 1} width={width * zoom} />
              </div>
            ))}
          </Document>
        )}
      </div>
    </div>
  );
}
