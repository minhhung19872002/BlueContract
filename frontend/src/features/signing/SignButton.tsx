import { Alert, App, Button, Input, Modal, Segmented, Typography, Upload } from 'antd';
import { Check, CircleCheck, Info, PenLine, Smartphone, Usb } from 'lucide-react';
import { useRef, useState } from 'react';
import {
  beginSign,
  completeSigningRequest,
  useInvalidateWorkflow,
  useSigningProviders,
  type SigningRequestDto,
} from '../../api/signing';
import { toApiProblem } from '../../api/http';
import {
  certificateFileToBase64,
  readTokenCertificate,
  signWithToken,
  tokenPluginConfigured,
} from './tokenPlugin';
import { waitForRemoteSignature } from './signingFlow';
import { providerIcon, savedProviderCode } from './providers';
import './signModal.css';

interface Props {
  stepId: string;
  label?: string;
  comment?: string;
  /** Shown under the modal title (document number). */
  documentCode?: string;
  onSigned?: () => void;
}

/** 0 = choose provider, 1 = connecting, 2 = signing (waiting phone / token), 3 = signature embedded. */
type Phase = 0 | 1 | 2 | 3;

interface TokenState {
  request?: SigningRequestDto;
  certificate?: string;
  signature?: string;
  busy?: boolean;
}

const PHASES = ['Đang kết nối', 'Đang ký', 'Đã gắn chữ ký vào PDF'];

/** Readable error for the USB-token plugin: "Failed to fetch" means the local plugin is not running. */
function tokenError(e: unknown) {
  const raw = e instanceof Error ? e.message : '';
  if (/failed to fetch|networkerror|load failed/i.test(raw)) {
    return 'Không kết nối được plugin ký số trên máy. Hãy cắm USB token, mở phần mềm ký số của nhà cung cấp chứng thư rồi thử lại.';
  }
  return raw || toApiProblem(e).title;
}

/**
 * "Ký số" for a workflow step (spec §8, design/Sign Modal.dc.html): a modal to pick the signing provider, then a 3-step
 * progress — remote signing waits for the phone confirmation (polling), USB token drives the local plugin.
 */
export function SignButton({ stepId, label = 'Ký số', comment, documentCode, onSigned }: Props) {
  const providers = useSigningProviders();
  const invalidate = useInvalidateWorkflow();
  const { message } = App.useApp();
  const [open, setOpen] = useState(false);
  const [phase, setPhase] = useState<Phase>(0);
  const [providerCode, setProviderCode] = useState(savedProviderCode);
  const [remoteMessage, setRemoteMessage] = useState<string | null>(null);
  const [token, setToken] = useState<TokenState | null>(null);
  const remoteWait = useRef({ cancelled: false });

  const list = providers.data ?? [];
  const preferred = list.find((p) => p.isDefault) ?? list[0];
  const provider = list.find((p) => p.code === providerCode) ?? preferred;

  const reset = () => {
    setPhase(0);
    setToken(null);
    setRemoteMessage(null);
  };
  const close = () => {
    remoteWait.current.cancelled = true;
    setOpen(false);
    reset();
  };

  const finish = async (result: SigningRequestDto) => {
    if (result.status === 'Completed') {
      setPhase(3);
      message.success('Đã ký số thành công');
      await invalidate();
      setOpen(false);
      reset();
      onSigned?.();
    } else {
      message.error(
        result.error ?? (result.status === 'Expired' ? 'Yêu cầu ký đã hết hạn.' : 'Ký số không thành công.'),
      );
      reset();
      await invalidate();
    }
  };

  const start = async () => {
    if (!provider) return;
    if (provider.mode === 'ClientSide') {
      setToken({});
      return;
    }
    setPhase(1);
    try {
      const request = await beginSign(stepId, { provider: provider.code, comment });
      if (request.status === 'Pending' && request.mode === 'RemoteAsync') {
        const wait = { cancelled: false };
        remoteWait.current = wait;
        setRemoteMessage(request.message ?? null);
        setPhase(2);
        const result = await waitForRemoteSignature(request, () => wait.cancelled);
        if (!wait.cancelled) await finish(result);
      } else {
        setPhase(2);
        await finish(request);
      }
    } catch (e) {
      message.error(toApiProblem(e).title);
      reset();
    }
  };

  // ---- USB token ----
  const startToken = async (certificate: string) => {
    setToken({ certificate, busy: true });
    setPhase(1);
    try {
      const request = await beginSign(stepId, {
        provider: 'USB_TOKEN',
        clientCertificate: certificate,
        comment,
      });
      if (request.status !== 'Pending' || !request.dataToSign) {
        await finish(request);
        return;
      }
      setPhase(2);
      if (tokenPluginConfigured) {
        const signature = await signWithToken(request.dataToSign);
        await finish(await completeSigningRequest(request.requestId, signature));
        return;
      }
      setToken({ certificate, request, busy: false });
    } catch (e) {
      message.error(tokenError(e));
      setToken({ certificate, busy: false });
      setPhase(0);
    }
  };

  const submitManualSignature = async () => {
    if (!token?.request || !token.signature) return;
    setToken({ ...token, busy: true });
    try {
      await finish(await completeSigningRequest(token.request.requestId, token.signature.trim()));
    } catch (e) {
      message.error(toApiProblem(e).title);
      setToken({ ...token, busy: false });
    }
  };

  const choosing = phase === 0 && !token;
  const progress = phase > 0 && !(token && !token.busy && token.request);

  return (
    <>
      <Button
        type="primary"
        size="small"
        icon={<PenLine size={14} strokeWidth={1.75} aria-hidden />}
        loading={providers.isLoading}
        disabled={!preferred}
        onClick={() => {
          setProviderCode(preferred?.code);
          setOpen(true);
        }}
      >
        {label}
      </Button>

      <Modal
        open={open}
        width={500}
        maskClosable={false}
        onCancel={close}
        className="sign-modal"
        title={
          <div>
            <div className="sign-modal__title">Ký số tài liệu</div>
            {documentCode && <div className="sign-modal__code mono">{documentCode}</div>}
          </div>
        }
        footer={
          <>
            <Button onClick={close}>
              {phase === 2 && provider?.mode === 'RemoteAsync' ? 'Đóng (yêu cầu vẫn chờ xác nhận)' : 'Hủy'}
            </Button>
            {choosing && (
              <Button
                type="primary"
                icon={<PenLine size={16} strokeWidth={1.75} aria-hidden />}
                onClick={() => void start()}
                disabled={!provider}
              >
                {label}
              </Button>
            )}
            {token?.request && !token.busy && (
              <Button type="primary" disabled={!token.signature} onClick={() => void submitManualSignature()}>
                Hoàn tất ký
              </Button>
            )}
          </>
        }
      >
        {choosing && (
          <div className="sign-modal__form">
            <div className="sign-modal__field">
              <span className="sign-modal__label">Nhà cung cấp chữ ký số</span>
              {list.length > 1 ? (
                <Segmented
                  block
                  value={provider?.code}
                  onChange={(v) => setProviderCode(String(v))}
                  options={list.map((p) => ({ value: p.code, label: p.displayName, icon: providerIcon(p) }))}
                />
              ) : (
                <span className="sign-modal__provider">
                  {providerIcon(provider)}
                  {provider?.displayName}
                </span>
              )}
            </div>
            <div className="sign-modal__note">
              <Info size={15} strokeWidth={1.75} aria-hidden />
              <span>
                Chữ ký được gắn trực tiếp vào tệp PDF.{' '}
                {provider?.mode === 'ClientSide'
                  ? 'Hãy cắm USB token trước khi ký.'
                  : provider?.mode === 'RemoteAsync'
                    ? 'Sau khi bấm ký, xác nhận yêu cầu trên ứng dụng ký số của điện thoại.'
                    : ''}
              </span>
            </div>
          </div>
        )}

        {token && !token.request && !token.busy && (
          <div className="sign-modal__form">
            {tokenPluginConfigured ? (
              <Button
                type="primary"
                icon={<Usb size={16} strokeWidth={1.75} aria-hidden />}
                onClick={async () => {
                  try {
                    await startToken(await readTokenCertificate());
                  } catch (e) {
                    message.error(tokenError(e));
                  }
                }}
              >
                Đọc chứng thư từ USB token và ký
              </Button>
            ) : (
              <Alert
                type="info"
                showIcon
                message="Chưa cấu hình plugin ký số của nhà cung cấp chứng thư"
                description="Chọn tệp chứng thư (.cer) của USB token; hệ thống trả về dữ liệu cần ký để ký bằng công cụ của CA, rồi dán chữ ký vào bước sau."
              />
            )}
            <Upload
              accept=".cer,.crt,.pem,.der"
              showUploadList={false}
              beforeUpload={(file) => {
                void certificateFileToBase64(file).then(startToken);
                return false;
              }}
            >
              <Button>Chọn tệp chứng thư (.cer)</Button>
            </Upload>
          </div>
        )}

        {token?.request && !token.busy && (
          <div className="sign-modal__form">
            <Typography.Text>Dữ liệu cần ký (base64, SHA-256 / RSA PKCS#1 v1.5):</Typography.Text>
            <Typography.Paragraph
              code
              copyable
              style={{ wordBreak: 'break-all', maxHeight: 120, overflow: 'auto' }}
            >
              {token.request.dataToSign}
            </Typography.Paragraph>
            <Input.TextArea
              rows={4}
              placeholder="Dán giá trị chữ ký (base64) do plugin/công cụ của CA trả về"
              value={token.signature}
              onChange={(e) => setToken({ ...token, signature: e.target.value })}
            />
          </div>
        )}

        {progress && (
          <div className="sign-modal__progress" aria-live="polite">
            {PHASES.map((l, i) => {
              const done = phase > i + 1 || phase === 3;
              const active = phase === i + 1 && phase !== 3;
              return (
                <div
                  key={l}
                  className={`sign-modal__phase${done ? ' sign-modal__phase--done' : ''}${active ? ' sign-modal__phase--active' : ''}`}
                >
                  <span className="sign-modal__phase-dot">
                    {done ? (
                      <Check size={14} strokeWidth={2.5} aria-hidden />
                    ) : active ? (
                      <span className="sign-modal__spinner" aria-hidden />
                    ) : null}
                  </span>
                  <span>{i === 1 && phase === 2 && remoteMessage ? remoteMessage : l}</span>
                </div>
              );
            })}
            {phase === 2 && provider?.mode === 'RemoteAsync' && (
              <div className="sign-modal__note">
                <Smartphone size={15} strokeWidth={1.75} aria-hidden />
                <span>Hệ thống tự cập nhật khi anh/chị xác nhận trên ứng dụng ký số.</span>
              </div>
            )}
            {phase === 3 && (
              <div className="sign-modal__success">
                <CircleCheck size={17} strokeWidth={1.75} aria-hidden />
                <span>
                  <strong>Đã ký thành công.</strong> Luồng ký chuyển sang bước tiếp theo.
                </span>
              </div>
            )}
          </div>
        )}
      </Modal>
    </>
  );
}
