import dayjs from '../i18n/dayjs';
import { authStore, type CurrentUser } from '../auth/authStore';
import { draftKey, fromStorable, readDraft, removeDraft, toStorable, writeDraft } from './useFormDraft';

const user = (id: string) =>
  ({ id, userName: id, permissions: [], mustChangePassword: false }) as unknown as CurrentUser;

describe('form drafts', () => {
  afterEach(() => {
    window.localStorage.clear();
    authStore.clear();
  });

  it('round-trips dates, nested lists and drops files and undefined', () => {
    const values = {
      name: 'Hợp đồng',
      signedDate: dayjs('2026-10-17T03:00:00Z'),
      lines: [{ qty: 2, expiry: dayjs('2027-01-31T00:00:00Z') }],
      attachment: new File(['x'], 'a.pdf'),
      password: 'Secret@123',
      account: { userName: 'ncc', clientSecret: 'x' },
      note: undefined,
    };
    const restored = fromStorable(JSON.parse(JSON.stringify(toStorable(values)))) as typeof values;
    expect(restored.name).toBe('Hợp đồng');
    expect(dayjs.isDayjs(restored.signedDate)).toBe(true);
    expect(restored.signedDate.toISOString()).toBe('2026-10-17T03:00:00.000Z');
    expect(restored.lines[0]!.expiry.toISOString()).toBe('2027-01-31T00:00:00.000Z');
    expect('attachment' in restored).toBe(false);
    expect('note' in restored).toBe(false);
    expect('password' in restored).toBe(false);
    expect(restored.account).toEqual({ userName: 'ncc' });
  });

  it('keeps drafts per user and per form', () => {
    authStore.setSession('t', user('u1'));
    writeDraft('receipt-new', { a: 1 }, [{ id: 'x' }]);
    expect(readDraft('receipt-new')?.values).toEqual({ a: 1 });
    expect(readDraft('receipt-new')?.extra).toEqual([{ id: 'x' }]);
    expect(readDraft('proposal-new')).toBeNull();

    authStore.setSession('t', user('u2'));
    expect(readDraft('receipt-new')).toBeNull();
    expect(draftKey('receipt-new')).toContain('u2');
  });

  it('drops drafts older than 14 days and removes on request', () => {
    authStore.setSession('t', user('u1'));
    writeDraft('old', { a: 1 });
    expect(readDraft('old', Date.now() + 15 * 24 * 3600 * 1000)).toBeNull();
    expect(window.localStorage.getItem(draftKey('old'))).toBeNull();

    writeDraft('kept', { a: 1 });
    removeDraft('kept');
    expect(readDraft('kept')).toBeNull();
  });
});
