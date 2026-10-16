import { Alert, Button, Space } from 'antd';
import type { FormInstance } from 'antd';
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import dayjs from '../i18n/dayjs';
import { authStore } from '../auth/authStore';
import { formatDateTime } from '../utils/format';

const PREFIX = 'blueprocure.draft.v1.';
const MAX_AGE_MS = 14 * 24 * 3600 * 1000;
const DEBOUNCE_MS = 500;

interface StoredDraft {
  savedAt: string;
  values: unknown;
  extra?: unknown;
}

/** Secrets never go to browser storage, whatever form they are typed in. */
const SECRET_KEY = /password|secret/i;

/** dayjs values become { $day: iso } so they survive JSON; secrets, files and undefined are dropped. */
export function toStorable(value: unknown): unknown {
  if (dayjs.isDayjs(value)) return { $day: value.toISOString() };
  if (Array.isArray(value)) return value.map(toStorable);
  if (value && typeof value === 'object' && !(value instanceof File) && !(value instanceof Blob)) {
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>)
        .filter(
          ([k, v]) => v !== undefined && !SECRET_KEY.test(k) && !(v instanceof File) && !(v instanceof Blob),
        )
        .map(([k, v]) => [k, toStorable(v)]),
    );
  }
  return value;
}

export function fromStorable(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(fromStorable);
  if (value && typeof value === 'object') {
    const record = value as Record<string, unknown>;
    if (typeof record.$day === 'string' && Object.keys(record).length === 1) return dayjs(record.$day);
    return Object.fromEntries(Object.entries(record).map(([k, v]) => [k, fromStorable(v)]));
  }
  return value;
}

// The contract wizard kept its draft under this key before drafts were shared by every long form (AUDIT-1 §12.1).
try {
  localStorage.removeItem('blueprocure.contract-wizard-draft');
} catch {
  // storage unavailable
}

/** Storage key of a form's draft for the signed-in user (drafts never cross accounts on a shared computer). */
export function draftKey(key: string) {
  return `${PREFIX}${authStore.getSnapshot().user?.id ?? 'anonymous'}.${key}`;
}

export function readDraft(key: string, now = Date.now()): StoredDraft | null {
  try {
    const raw = localStorage.getItem(draftKey(key));
    if (!raw) return null;
    const draft = JSON.parse(raw) as StoredDraft;
    if (!draft.savedAt || now - new Date(draft.savedAt).getTime() > MAX_AGE_MS) {
      localStorage.removeItem(draftKey(key));
      return null;
    }
    return draft;
  } catch {
    return null; // storage unavailable or corrupted
  }
}

export function writeDraft(key: string, values: unknown, extra?: unknown) {
  try {
    const draft: StoredDraft = {
      savedAt: new Date().toISOString(),
      values: toStorable(values),
      extra: toStorable(extra),
    };
    localStorage.setItem(draftKey(key), JSON.stringify(draft));
  } catch {
    // quota exceeded / private mode: autosave is best effort
  }
}

export function removeDraft(key: string) {
  try {
    localStorage.removeItem(draftKey(key));
  } catch {
    // ignore
  }
}

/**
 * Lưu nháp tự động (spec §9.3) for a long form: values (and optional extra state such as goods lines) are saved to
 * the browser while the user types; when the form is opened again a banner offers to restore or discard the draft.
 * Pass `key: null` to disable (e.g. while editing a saved record). Call `clear()` after a successful save.
 */
export function useFormDraft<V, X = undefined>({
  key,
  form,
  extra,
  onRestoreExtra,
}: {
  key: string | null;
  form: FormInstance<V>;
  extra?: X;
  onRestoreExtra?: (extra: X) => void;
}): { onValuesChange: () => void; touch: () => void; clear: () => void; banner: ReactNode } {
  // The draft offered for the current key; re-read when the key changes (e.g. a dialog switching to "new").
  const [offered, setOffered] = useState<{ key: string | null; draft: StoredDraft | null }>(() => ({
    key,
    draft: key ? readDraft(key) : null,
  }));
  if (offered.key !== key) setOffered({ key, draft: key ? readDraft(key) : null });
  const pending = offered.key === key ? offered.draft : null;
  const setPending = useCallback((draft: StoredDraft | null) => setOffered({ key, draft }), [key]);
  const dirty = useRef(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const extraRef = useRef(extra);

  const schedule = useCallback(() => {
    if (!key) return;
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => {
      if (dirty.current) writeDraft(key, form.getFieldsValue(true), extraRef.current);
    }, DEBOUNCE_MS);
  }, [key, form]);

  // Extra state (lines…) is saved with the values once the user has edited something; pages call `touch()` from
  // their own edit handlers, since extra state also changes when data loads.
  useEffect(() => {
    extraRef.current = extra;
    if (dirty.current) schedule();
  }, [extra, schedule]);

  // A new key starts clean (nothing typed yet for it).
  useEffect(() => {
    dirty.current = false;
    return () => {
      if (timer.current) clearTimeout(timer.current);
    };
  }, [key]);

  const onValuesChange = useCallback(() => {
    dirty.current = true;
    schedule();
  }, [schedule]);

  const clear = useCallback(() => {
    if (timer.current) clearTimeout(timer.current);
    dirty.current = false;
    if (key) removeDraft(key);
    setPending(null);
  }, [key, setPending]);

  const restore = () => {
    if (!pending) return;
    form.setFieldsValue(fromStorable(pending.values) as Parameters<typeof form.setFieldsValue>[0]);
    if (pending.extra !== undefined && onRestoreExtra) onRestoreExtra(fromStorable(pending.extra) as X);
    dirty.current = true;
    setPending(null);
  };

  const banner = pending ? (
    <Alert
      type="info"
      showIcon
      style={{ marginBottom: 12 }}
      message={`Có bản nháp chưa lưu từ ${formatDateTime(pending.savedAt)}.`}
      action={
        <Space>
          <Button size="small" type="primary" onClick={restore}>
            Khôi phục
          </Button>
          <Button size="small" onClick={clear}>
            Bỏ
          </Button>
        </Space>
      }
    />
  ) : null;

  return { onValuesChange, touch: onValuesChange, clear, banner };
}
