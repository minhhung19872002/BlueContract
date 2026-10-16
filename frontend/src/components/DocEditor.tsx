import { Alert } from 'antd';
import { DocumentEditor } from '@onlyoffice/document-editor-react';
import { useState } from 'react';
import type { EditorSessionDto } from '../api/documents';

/**
 * ONLYOFFICE Docs editor (spec §2). The server signs the config (JWT) and decides the mode:
 * "review" forces track changes — deletions struck through, insertions highlighted.
 */
export function DocEditor({ session, height = '80vh' }: { session: EditorSessionDto; height?: string }) {
  const [error, setError] = useState<string | null>(null);
  const serverUrl = new URL(session.editorScriptUrl).origin + '/';
  const config = session.config as Record<string, unknown>;

  return (
    <div style={{ height }}>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 8 }} />}
      <DocumentEditor
        id={`editor-${session.sessionId}`}
        documentServerUrl={serverUrl}
        config={config}
        onLoadComponentError={(code: number, description: string) =>
          setError(
            code === -2
              ? 'Không kết nối được máy chủ soạn thảo ONLYOFFICE. Vui lòng kiểm tra dịch vụ.'
              : `Không mở được trình soạn thảo: ${description}`,
          )
        }
      />
    </div>
  );
}
