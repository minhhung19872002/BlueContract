import { renderToStaticMarkup } from 'react-dom/server';
import { Timeline, initials } from './Timeline';

describe('Timeline', () => {
  it('shows who, what, when and the note; actor initials in the avatar', () => {
    const html = renderToStaticMarkup(
      <Timeline
        items={[
          {
            key: 1,
            tone: 'danger',
            who: 'Nguyễn Văn An',
            context: 'Bệnh viện hủy đơn gọi hàng',
            at: '17/10/2026 08:30',
            note: 'Khoa đổi nhu cầu',
          },
          { key: 2, tone: 'teal', who: null, context: 'Phiếu nhập kho NK-01' },
        ]}
      />,
    );
    expect(html).toContain('Nguyễn Văn An');
    expect(html).toContain('VA');
    expect(html).toContain('Khoa đổi nhu cầu');
    expect(html).toContain('Hệ thống');
    expect(html).toContain('var(--bp-danger-bg)');
  });

  it('says so when empty', () => {
    expect(renderToStaticMarkup(<Timeline items={[]} empty="Chưa có ý kiến" />)).toContain('Chưa có ý kiến');
    expect(initials(undefined)).toBe('HT');
  });
});
