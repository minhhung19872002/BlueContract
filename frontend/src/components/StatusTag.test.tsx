import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { StatusTag, statusLabel, statusOptions, statusTone } from './StatusTag';

const TONES = [
  'primary',
  'success',
  'warning',
  'orange',
  'danger',
  'info',
  'purple',
  'indigo',
  'neutral',
  'teal',
];

describe('StatusTag (design system tones)', () => {
  it('keeps the Vietnamese labels and gives every status a known tone', () => {
    expect(statusLabel('contractStatus', 'Effective')).toBe('Đang hiệu lực');
    for (const domain of [
      'contractStatus',
      'receiptStatus',
      'receiptSource',
      'workflowAction',
      'callOrderStatus',
    ] as const) {
      for (const { value } of statusOptions(domain)) {
        expect(TONES).toContain(statusTone(domain, value));
      }
    }
  });

  it('applies the tone exceptions of the theme guide', () => {
    expect(statusTone('procurementType', 'NonBidding')).toBe('purple');
    expect(statusTone('receiptSource', 'His')).toBe('teal');
    expect(statusTone('contractStatus', 'Liquidated')).toBe('info');
    expect(statusTone('flag', 'Overdue')).toBe('danger');
  });

  it('shows a dot for statuses, a pulse while running, none for categories and a dashed virtual receipt', () => {
    const running = renderToStaticMarkup(<StatusTag domain="receiptStatus" value="Signing" suffix=" 3/5" />);
    expect(running).toContain('status-tag--running');
    expect(running).toContain('status-tag__dot');
    expect(running).toContain('Đang ký 3/5');

    const category = renderToStaticMarkup(<StatusTag domain="procurementType" value="Bidding" />);
    expect(category).not.toContain('status-tag__dot');

    expect(renderToStaticMarkup(<StatusTag domain="receiptSource" value="VirtualPortal" />)).toContain(
      'status-tag--dashed',
    );
  });
});
