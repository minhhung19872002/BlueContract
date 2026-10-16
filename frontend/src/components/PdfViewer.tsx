import { Alert, Empty, Spin, Tooltip } from 'antd';
import { useQuery } from '@tanstack/react-query';
import {
  ChevronLeft,
  ChevronRight,
  Download,
  Maximize,
  Minimize,
  Printer,
  ShieldAlert,
  ShieldCheck,
  ZoomIn,
  ZoomOut,
} from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Document, Page, pdfjs } from 'react-pdf';
import 'react-pdf/dist/Page/TextLayer.css';
import 'react-pdf/dist/Page/AnnotationLayer.css';
import { toApiProblem } from '../api/http';
import './pdfViewer.css';

pdfjs.GlobalWorkerOptions.workerSrc = new URL(
  'pdfjs-dist/build/pdf.worker.min.mjs',
  import.meta.url,
).toString();

interface Props {
  /** Loads the PDF; re-run whenever `cacheKey` changes. */
  load: () => Promise<Blob>;
  cacheKey: string;
  height?: number | string;
  /** SHA-256 check of the stored file: true "Toàn vẹn ✓", false "Sai mã", undefined = not shown. */
  integrity?: boolean | null;
  /** Name for "Tải xuống" (default: tai-lieu.pdf). */
  fileName?: string;
}

/**
 * In-app PDF viewer (pdf.js, design/Page Detail.dc.html): fits the container width, toolbar with pages, zoom, integrity,
 * download, print and full screen — usable on tablets for signers (spec §2).
 */
export function PdfViewer({ load, cacheKey, height = '75vh', integrity, fileName = 'tai-lieu.pdf' }: Props) {
  const file = useQuery({
    queryKey: ['pdf-viewer', cacheKey],
    queryFn: load,
    staleTime: 60_000,
    gcTime: 60_000,
  });
  const [renderError, setRenderError] = useState<string | null>(null);
  const [pages, setPages] = useState(0);
  const [current, setCurrent] = useState(1);
  const [zoom, setZoom] = useState(1);
  const [width, setWidth] = useState(800);
  const [fullscreen, setFullscreen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const scroller = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const el = scroller.current;
    if (!el) return;
    const observer = new ResizeObserver(([entry]) =>
      setWidth(Math.max(280, Math.min(900, (entry?.contentRect.width ?? 800) - 32))),
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    const onChange = () => setFullscreen(document.fullscreenElement === root.current);
    document.addEventListener('fullscreenchange', onChange);
    return () => document.removeEventListener('fullscreenchange', onChange);
  }, []);

  // Current page = the page whose top is nearest above the middle of the viewport.
  const onScroll = () => {
    const el = scroller.current;
    if (!el) return;
    const middle = el.scrollTop + el.clientHeight / 3;
    const nodes = Array.from(el.querySelectorAll<HTMLElement>('[data-page]'));
    const found = nodes.filter((n) => n.offsetTop <= middle).at(-1);
    if (found) setCurrent(Number(found.dataset.page));
  };

  const goTo = (page: number) => {
    const target = scroller.current?.querySelector<HTMLElement>(`[data-page="${page}"]`);
    if (target && scroller.current) {
      scroller.current.scrollTo({ top: target.offsetTop - 12, behavior: 'smooth' });
      setCurrent(page);
    }
  };

  const withBlobUrl = (action: (url: string) => void) => {
    if (!file.data) return;
    const url = URL.createObjectURL(file.data);
    action(url);
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  };

  const download = () =>
    withBlobUrl((url) => {
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName;
      a.click();
    });

  const print = () =>
    withBlobUrl((url) => {
      const frame = document.createElement('iframe');
      frame.style.position = 'fixed';
      frame.style.width = '0';
      frame.style.height = '0';
      frame.style.border = '0';
      frame.src = url;
      frame.onload = () => frame.contentWindow?.print();
      document.body.appendChild(frame);
      setTimeout(() => frame.remove(), 60_000);
    });

  const toggleFullscreen = () => {
    if (document.fullscreenElement) void document.exitFullscreen();
    else void root.current?.requestFullscreen?.();
  };

  const error = file.isError ? toApiProblem(file.error).title : renderError;

  return (
    <div ref={root} className={`pdf-viewer${fullscreen ? ' pdf-viewer--fullscreen' : ''}`}>
      <div className="pdf-viewer__toolbar" role="toolbar" aria-label="Thanh công cụ PDF">
        <button
          type="button"
          className="pdf-viewer__btn pdf-viewer__btn--bordered"
          aria-label="Trang trước"
          disabled={current <= 1}
          onClick={() => goTo(current - 1)}
        >
          <ChevronLeft size={15} aria-hidden />
        </button>
        <span className="pdf-viewer__pages">
          Trang <strong>{pages ? current : 0}</strong> / {pages}
        </span>
        <button
          type="button"
          className="pdf-viewer__btn pdf-viewer__btn--bordered"
          aria-label="Trang sau"
          disabled={current >= pages}
          onClick={() => goTo(current + 1)}
        >
          <ChevronRight size={15} aria-hidden />
        </button>
        <span className="pdf-viewer__divider" aria-hidden />
        <button
          type="button"
          className="pdf-viewer__btn"
          aria-label="Thu nhỏ"
          onClick={() => setZoom((z) => Math.max(0.5, z - 0.25))}
        >
          <ZoomOut size={15} aria-hidden />
        </button>
        <span className="pdf-viewer__zoom">{Math.round(zoom * 100)}%</span>
        <button
          type="button"
          className="pdf-viewer__btn"
          aria-label="Phóng to"
          onClick={() => setZoom((z) => Math.min(3, z + 0.25))}
        >
          <ZoomIn size={15} aria-hidden />
        </button>
        <span className="pdf-viewer__spacer" />
        {integrity === true && (
          <Tooltip title="Mã SHA-256 của tệp khớp với giá trị ghi khi lưu">
            <span className="pdf-viewer__integrity">
              <ShieldCheck size={13} aria-hidden />
              Toàn vẹn ✓
            </span>
          </Tooltip>
        )}
        {integrity === false && (
          <span className="pdf-viewer__integrity pdf-viewer__integrity--bad">
            <ShieldAlert size={13} aria-hidden />
            Sai mã toàn vẹn
          </span>
        )}
        <Tooltip title="Tải xuống">
          <button
            type="button"
            className="pdf-viewer__btn"
            aria-label="Tải xuống"
            disabled={!file.data}
            onClick={download}
          >
            <Download size={15} aria-hidden />
          </button>
        </Tooltip>
        <Tooltip title="In">
          <button
            type="button"
            className="pdf-viewer__btn"
            aria-label="In"
            disabled={!file.data}
            onClick={print}
          >
            <Printer size={15} aria-hidden />
          </button>
        </Tooltip>
        <Tooltip title={fullscreen ? 'Thoát toàn màn hình' : 'Toàn màn hình'}>
          <button
            type="button"
            className="pdf-viewer__btn"
            aria-label={fullscreen ? 'Thoát toàn màn hình' : 'Toàn màn hình'}
            onClick={toggleFullscreen}
          >
            {fullscreen ? <Minimize size={15} aria-hidden /> : <Maximize size={15} aria-hidden />}
          </button>
        </Tooltip>
      </div>
      <div
        ref={scroller}
        className="pdf-viewer__canvas"
        style={{ height: fullscreen ? undefined : height }}
        onScroll={onScroll}
      >
        {error && <Alert type="error" showIcon message={error} />}
        {!error && file.isPending && <Spin style={{ marginTop: 48 }} />}
        {file.data && (
          <Document
            file={file.data}
            onLoadSuccess={({ numPages }) => {
              setPages(numPages);
              setCurrent(1);
            }}
            onLoadError={() => setRenderError('Tệp PDF không hợp lệ.')}
            loading={<Spin />}
            noData={<Empty description="Không có dữ liệu" />}
          >
            {Array.from({ length: pages }, (_, i) => (
              <div key={i} data-page={i + 1} className="pdf-viewer__page">
                <Page pageNumber={i + 1} width={width * zoom} />
              </div>
            ))}
          </Document>
        )}
      </div>
    </div>
  );
}
