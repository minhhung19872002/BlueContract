import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { Form, Input } from 'antd';
import { authStore, type CurrentUser } from '../auth/authStore';
import { useFormDraft } from './useFormDraft';

function DraftForm() {
  const [form] = Form.useForm<{ note?: string }>();
  const draft = useFormDraft({ key: 'test-form', form });
  return (
    <>
      {draft.banner}
      <Form form={form} onValuesChange={draft.onValuesChange}>
        <Form.Item name="note" label="Ghi chú">
          <Input />
        </Form.Item>
      </Form>
      <button type="button" onClick={draft.clear}>
        Lưu xong
      </button>
    </>
  );
}

describe('useFormDraft in a form', () => {
  beforeEach(() => {
    authStore.setSession('t', {
      id: 'u1',
      userName: 'u1',
      permissions: [],
      mustChangePassword: false,
    } as unknown as CurrentUser);
  });
  afterEach(() => {
    vi.useRealTimers();
    localStorage.clear();
    authStore.clear();
  });

  it('saves what is typed, offers it when the form opens again and restores it', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const first = render(<DraftForm />);
    expect(screen.queryByText(/Có bản nháp/)).toBeNull();
    fireEvent.change(screen.getByLabelText('Ghi chú'), { target: { value: 'Giao đợt 2' } });
    await act(async () => {
      vi.advanceTimersByTime(600);
    });
    first.unmount();

    render(<DraftForm />);
    expect(await screen.findByText(/Có bản nháp chưa lưu/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Khôi phục' }));
    await waitFor(() => expect(screen.getByLabelText('Ghi chú')).toHaveValue('Giao đợt 2'));
    expect(screen.queryByText(/Có bản nháp/)).toBeNull();
  });

  it('forgets the draft after a successful save', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const first = render(<DraftForm />);
    fireEvent.change(screen.getByLabelText('Ghi chú'), { target: { value: 'x' } });
    await act(async () => {
      vi.advanceTimersByTime(600);
    });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu xong' }));
    first.unmount();

    render(<DraftForm />);
    expect(screen.queryByText(/Có bản nháp/)).toBeNull();
  });
});
