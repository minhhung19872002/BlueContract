import { Input, Modal } from 'antd';
import { CornerDownLeft, FileText, Search } from 'lucide-react';
import { useMemo, useRef, useState, type KeyboardEvent } from 'react';
import type { InputRef } from 'antd';
import type { FlatNavItem } from '../layouts/AppShell';

interface Command {
  key: string;
  label: string;
  hint: string;
  icon?: React.ReactNode;
  path: string;
}

/** Vietnamese-insensitive matching: "hop dong" finds "Hợp đồng". */
const fold = (s: string) =>
  s
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .replace(/đ/g, 'd')
    .replace(/Đ/g, 'D')
    .toLowerCase();

/** Ctrl+K palette (design/App Shell.dc.html): jump to any page of the menu, or search contracts by number / name / supplier. */
export function CommandPalette({
  open,
  onClose,
  pages,
  contractsPath,
  onNavigate,
}: {
  open: boolean;
  onClose: () => void;
  pages: FlatNavItem[];
  contractsPath: string;
  onNavigate: (path: string) => void;
}) {
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);
  const input = useRef<InputRef>(null);

  // Fresh palette on every opening; the input takes the focus once the modal is shown.
  const reset = () => {
    setQuery('');
    setActive(0);
  };

  const commands = useMemo<Command[]>(() => {
    const q = fold(query.trim());
    const matches = pages
      .filter((p) => !q || fold(p.label).includes(q) || fold(p.group).includes(q))
      .slice(0, 12)
      .map((p) => ({ key: p.key, label: p.label, hint: p.group, icon: p.icon, path: p.key }));
    return query.trim()
      ? [
          {
            key: 'search-contracts',
            label: `Tìm hợp đồng “${query.trim()}”`,
            hint: 'Hợp đồng',
            icon: <FileText size={16} strokeWidth={1.75} aria-hidden />,
            path: `${contractsPath}?search=${encodeURIComponent(query.trim())}`,
          },
          ...matches,
        ]
      : matches;
  }, [pages, query, contractsPath]);

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setActive((a) => Math.min(a + 1, commands.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActive((a) => Math.max(a - 1, 0));
    } else if (e.key === 'Enter' && commands[active]) {
      e.preventDefault();
      reset();
      onNavigate(commands[active].path);
    }
  };

  return (
    <Modal
      open={open}
      onCancel={() => {
        reset();
        onClose();
      }}
      afterOpenChange={(visible) => visible && input.current?.focus()}
      footer={null}
      closable={false}
      width={560}
      className="command-palette"
      destroyOnHidden
    >
      <Input
        ref={input}
        size="large"
        variant="borderless"
        prefix={<Search size={18} strokeWidth={1.75} aria-hidden />}
        placeholder="Tìm trang hoặc hợp đồng…"
        value={query}
        onChange={(e) => {
          setQuery(e.target.value);
          setActive(0);
        }}
        onKeyDown={onKeyDown}
        aria-label="Tìm kiếm nhanh"
        aria-controls="command-palette-list"
      />
      <div id="command-palette-list" role="listbox" className="command-palette__list">
        {commands.length === 0 && <div className="command-palette__empty">Không tìm thấy trang phù hợp</div>}
        {commands.map((c, i) => (
          <button
            type="button"
            key={c.key}
            role="option"
            aria-selected={i === active}
            className={`command-palette__item${i === active ? ' command-palette__item--active' : ''}`}
            onMouseEnter={() => setActive(i)}
            onClick={() => {
              reset();
              onNavigate(c.path);
            }}
          >
            <span className="command-palette__icon">{c.icon}</span>
            <span className="command-palette__label">{c.label}</span>
            <span className="command-palette__hint">{c.hint}</span>
            {i === active && <CornerDownLeft size={14} strokeWidth={1.75} aria-hidden />}
          </button>
        ))}
      </div>
    </Modal>
  );
}
