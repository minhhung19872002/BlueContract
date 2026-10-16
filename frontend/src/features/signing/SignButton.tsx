import { Alert, App, Button, Dropdown, Input, Modal, Space, Spin, Steps, Typography, Upload } from 'antd';
import { EditOutlined, MobileOutlined, UsbOutlined } from '@ant-design/icons';
import { useRef, useState } from 'react';
import {
  beginSign,
  completeSigningRequest,
  useInvalidateWorkflow,
  useSigningProviders,
  type SigningProviderDto,
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

interface Props {
  stepId: string;
  label?: string;
  comment?: string;
  onSigned?: () => void;
}

type Phase =
  | { kind: 'idle' }
  | { kind: 'remote'; message?: string | null }
  | { kind: 'token'; request?: SigningRequestDto; certificate?: string; signature?: string; busy?: boolean };

/**
 * "Ký" button for a workflow step (spec §8): picks the provider (default first), signs immediately on the server,
 * waits for the phone confirmation (remote signing) with polling, or drives the USB-token plugin.
 */
export function SignButton({ stepId, label = 'Ký số', comment, onSigned }: Props) {
  const providers = useSigningProviders();
  const invalidate = useInvalidateWorkflow();
  const { message } = App.useApp();
  const [loading, setLoading] = useState(false);
  const [phase, setPhase] = useState<Phase>({ kind: 'idle' });
  const remoteWait = useRef({ cancelled: false });

  const list = providers.data ?? [];
  const preferred = list.find((p) => p.isDefault) ?? list[0];

  const finish = async (result: SigningRequestDto) => {
    if (result.status === 'Completed') {
      message.success('Đã ký số thành công');
      setPhase({ kind: 'idle' });
      await invalidate();
      onSigned?.();
    } else {
      message.error(
        result.error ?? (result.status === 'Expired' ? 'Yêu cầu ký đã hết hạn.' : 'Ký số không thành công.'),
      );
      setPhase({ kind: 'idle' });
      await invalidate();
    }
  };

  const run = async (provider: SigningProviderDto) => {
    if (provider.mode === 'ClientSide') {
      setPhase({ kind: 'token' });
      return;
    }
    setLoading(true);
    try {
      const request = await beginSign(stepId, { provider: provider.code, comment });
      if (request.status === 'Pending' && request.mode === 'RemoteAsync') {
        const wait = { cancelled: false };
        remoteWait.current = wait;
        setPhase({ kind: 'remote', message: request.message });
        const result = await waitForRemoteSignature(request, () => wait.cancelled);
        if (!wait.cancelled) await finish(result);
      } else {
        await finish(request);
      }
    } catch (e) {
      message.error(toApiProblem(e).title);
      setPhase({ kind: 'idle' });
    } finally {
      setLoading(false);
    }
  };

  // ---- USB token ----
  const tokenState = phase.kind === 'token' ? phase : null;

  const startToken = async (certificate: string) => {
    setPhase({ kind: 'token', certificate, busy: true });
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
      if (tokenPluginConfigured) {
        const signature = await signWithToken(request.dataToSign);
        await finish(await completeSigningRequest(request.requestId, signature));
        return;
      }
      setPhase({ kind: 'token', certificate, request, busy: false });
    } catch (e) {
      message.error(e instanceof Error ? e.message : toApiProblem(e).title);
      setPhase({ kind: 'token', certificate, busy: false });
    }
  };

  const submitManualSignature = async () => {
    if (!tokenState?.request || !tokenState.signature) return;
    setPhase({ ...tokenState, busy: true });
    try {
      await finish(await completeSigningRequest(tokenState.request.requestId, tokenState.signature.trim()));
    } catch (e) {
      message.error(toApiProblem(e).title);
      setPhase({ ...tokenState, busy: false });
    }
  };

  const icon = (p?: SigningProviderDto) =>
    p?.mode === 'RemoteAsync' ? (
      <MobileOutlined />
    ) : p?.mode === 'ClientSide' ? (
      <UsbOutlined />
    ) : (
      <EditOutlined />
    );

  return (
    <>
      {list.length > 1 ? (
        <Dropdown.Button
          type="primary"
          loading={loading}
          disabled={!preferred}
          onClick={() => preferred && void run(preferred)}
          menu={{
            items: list.map((p) => ({ key: p.code, icon: icon(p), label: p.displayName })),
            onClick: ({ key }) => {
              const p = list.find((x) => x.code === key);
              if (p) void run(p);
            },
          }}
        >
          {icon(preferred)} {label}
        </Dropdown.Button>
      ) : (
        <Button
          type="primary"
          icon={icon(preferred)}
          loading={loading || providers.isLoading}
          disabled={!preferred}
          onClick={() => preferred && void run(preferred)}
        >
          {label}
        </Button>
      )}

      <Modal
        open={phase.kind === 'remote'}
        title="Ký số từ xa"
        closable={false}
        maskClosable={false}
        footer={
          <Button
            onClick={() => {
              remoteWait.current.cancelled = true;
              setPhase({ kind: 'idle' });
            }}
          >
            Đóng (yêu cầu vẫn chờ xác nhận)
          </Button>
        }
      >
        <Space direction="vertical" align="center" style={{ width: '100%', padding: 16 }}>
          <Spin size="large" />
          <Typography.Text strong>
            {phase.kind === 'remote'
              ? (phase.message ?? 'Vui lòng xác nhận yêu cầu ký trên điện thoại.')
              : ''}
          </Typography.Text>
          <Typography.Text type="secondary">
            Hệ thống tự cập nhật khi anh/chị xác nhận trên ứng dụng ký số.
          </Typography.Text>
        </Space>
      </Modal>

      <Modal
        open={!!tokenState}
        title="Ký bằng USB token"
        onCancel={() => setPhase({ kind: 'idle' })}
        maskClosable={false}
        footer={
          tokenState?.request ? (
            <Button
              type="primary"
              loading={tokenState.busy}
              disabled={!tokenState.signature}
              onClick={() => void submitManualSignature()}
            >
              Hoàn tất ký
            </Button>
          ) : null
        }
        width={640}
      >
        <Steps
          size="small"
          current={tokenState?.request ? 1 : 0}
          style={{ marginBottom: 16 }}
          items={[{ title: 'Chọn chứng thư' }, { title: 'Ký trên token' }]}
        />
        {!tokenState?.request && (
          <Space direction="vertical" style={{ width: '100%' }}>
            {tokenPluginConfigured ? (
              <Button
                type="primary"
                icon={<UsbOutlined />}
                loading={tokenState?.busy}
                onClick={async () => {
                  try {
                    await startToken(await readTokenCertificate());
                  } catch (e) {
                    message.error(e instanceof Error ? e.message : 'Không đọc được chứng thư từ USB token.');
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
              <Button loading={tokenState?.busy && !tokenPluginConfigured}>Chọn tệp chứng thư (.cer)</Button>
            </Upload>
          </Space>
        )}
        {tokenState?.request && (
          <Space direction="vertical" style={{ width: '100%' }}>
            <Typography.Text>Dữ liệu cần ký (base64, SHA-256 / RSA PKCS#1 v1.5):</Typography.Text>
            <Typography.Paragraph
              code
              copyable
              style={{ wordBreak: 'break-all', maxHeight: 120, overflow: 'auto' }}
            >
              {tokenState.request.dataToSign}
            </Typography.Paragraph>
            <Input.TextArea
              rows={4}
              placeholder="Dán giá trị chữ ký (base64) do plugin/công cụ của CA trả về"
              value={tokenState.signature}
              onChange={(e) => setPhase({ ...tokenState, signature: e.target.value })}
            />
          </Space>
        )}
      </Modal>
    </>
  );
}
