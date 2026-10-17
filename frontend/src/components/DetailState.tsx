import { Button, Card } from 'antd';
import { FileX, RefreshCw, ServerCrash } from 'lucide-react';
import { toApiProblem } from '../api/http';
import { ErrorState, ForbiddenPage } from '../pages/ErrorPages';

/**
 * What a detail page shows before its record is there: a skeleton while loading, and once the request failed a
 * proper state instead of an empty card — not found (also what the API answers for another supplier's record),
 * no permission, or the server's message with a retry.
 */
export function DetailState({
  query,
}: {
  query: { isError: boolean; error: unknown; refetch?: () => unknown };
}) {
  if (!query.isError) return <Card loading />;
  const problem = toApiProblem(query.error);
  if (problem.status === 404) {
    return (
      <ErrorState
        code="404"
        tone="primary"
        icon={FileX}
        title="Không tìm thấy tài liệu"
        body="Tài liệu không tồn tại, đã bị xóa, hoặc tài khoản của anh/chị không được xem. Kiểm tra lại đường dẫn hoặc tìm bằng Ctrl+K."
      />
    );
  }
  if (problem.status === 403) return <ForbiddenPage />;
  return (
    <ErrorState
      code={problem.status ? String(problem.status) : '—'}
      tone="danger"
      icon={ServerCrash}
      title="Không tải được dữ liệu"
      body={problem.title}
      secondary={
        query.refetch && (
          <Button icon={<RefreshCw size={16} aria-hidden />} onClick={() => void query.refetch?.()}>
            Thử lại
          </Button>
        )
      }
    />
  );
}
