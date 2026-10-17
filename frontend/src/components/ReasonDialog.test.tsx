import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { ReasonDialog } from './ReasonDialog';

function renderDialog(onConfirm: (reason: string) => Promise<unknown>, onClose = vi.fn(), required = true) {
  render(
    <App>
      <ReasonDialog
        open
        title="Hủy đơn gọi hàng?"
        okText="Hủy đơn"
        reasonLabel="Lý do hủy"
        required={required}
        onClose={onClose}
        onConfirm={onConfirm}
      />
    </App>,
  );
  return onClose;
}

describe('ReasonDialog', () => {
  it('requires a reason before confirming, then passes it trimmed and closes', async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined);
    const onClose = renderDialog(onConfirm);

    fireEvent.click(screen.getByRole('button', { name: 'Hủy đơn' }));
    expect(await screen.findByText('Vui lòng nhập lý do')).toBeInTheDocument();
    expect(onConfirm).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText('Lý do hủy'), { target: { value: '  Khoa đổi nhu cầu  ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Hủy đơn' }));
    await waitFor(() => expect(onConfirm).toHaveBeenCalledWith('Khoa đổi nhu cầu', {}));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it('stays open with what was typed when the action fails', async () => {
    const onConfirm = vi.fn().mockRejectedValue(new Error('409'));
    const onClose = renderDialog(onConfirm);

    fireEvent.change(screen.getByLabelText('Lý do hủy'), { target: { value: 'Lý do' } });
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Hủy đơn' }));
    });
    await waitFor(() => expect(onConfirm).toHaveBeenCalled());
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Lý do hủy')).toHaveValue('Lý do');
  });

  it('accepts an empty opinion when not required', async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined);
    renderDialog(onConfirm, vi.fn(), false);
    fireEvent.click(screen.getByRole('button', { name: 'Hủy đơn' }));
    await waitFor(() => expect(onConfirm).toHaveBeenCalledWith('', {}));
  });
});
