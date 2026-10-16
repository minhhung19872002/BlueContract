import { Drawer, Dropdown, Grid, Layout, Menu, Tooltip, type MenuProps } from 'antd';
import {
  Building2,
  ChevronDown,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  House,
  LogOut,
  Menu as MenuIcon,
  Moon,
  Search,
  ShieldCheck,
  Sun,
} from 'lucide-react';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { useAuth } from '../auth/useAuth';
import { useInboxCount } from '../api/signing';
import { useInternalDashboard, useSupplierDashboard } from '../api/reports';
import { P } from '../auth/permissions';
import { NotificationBell } from '../components/NotificationBell';
import { CommandPalette } from '../components/CommandPalette';
import { portalLink, useRealtime } from '../realtime/useRealtime';
import { useThemeMode } from '../hooks/useThemeMode';
import './shell.css';

export type NavBadge = 'inbox' | 'callOrders' | 'receipts';

export interface NavItem {
  key: string;
  label: string;
  icon?: ReactNode;
  /** Visible if the user has ANY of these permissions (omit = always). */
  permissions?: string[];
  /** Counter shown next to the item (Trình ký: documents waiting; Gọi hàng / Phiếu nhập kho: items needing action). */
  badge?: NavBadge;
  children?: NavItem[];
}

export interface NavGroup {
  group: string;
  items: NavItem[];
}

export interface FlatNavItem {
  key: string;
  label: string;
  group: string;
  icon?: ReactNode;
}

function filterItems(items: NavItem[], can: (...p: string[]) => boolean): NavItem[] {
  return items
    .filter((i) => !i.permissions || can(...i.permissions))
    .map((i) => (i.children ? { ...i, children: filterItems(i.children, can) } : i))
    .filter((i) => !i.children || i.children.length > 0);
}

/** Every reachable page with its group — for the breadcrumb and the command palette. */
export function flattenNav(groups: NavGroup[]): FlatNavItem[] {
  return groups.flatMap((g) =>
    g.items.flatMap((i) =>
      i.children
        ? i.children.map((c) => ({ key: c.key, label: c.label, group: i.label, icon: i.icon }))
        : [{ key: i.key, label: i.label, group: g.group, icon: i.icon }],
    ),
  );
}

/** Pages reached from links only (no menu entry), for the breadcrumb. */
const PAGES_OUTSIDE_MENU: { path: string; group: string; label: string }[] = [
  { path: '/tai-lieu', group: 'Làm việc', label: 'Tài liệu' },
  { path: '/thong-bao', group: 'Làm việc', label: 'Thông báo' },
  { path: '/tai-khoan', group: 'Cá nhân', label: 'Tài khoản' },
];

function pageOutsideMenu(pathname: string) {
  const path = pathname.replace(/^\/ncc(?=\/)/, '');
  return PAGES_OUTSIDE_MENU.find((p) => path === p.path || path.startsWith(p.path + '/'));
}

/** Counters of the menu badges; the dashboard query is shared with the home page (same cache key). */
function useNavBadges(isSupplier: boolean, canInbox: boolean): Record<NavBadge, number> {
  const inbox = useInboxCount(canInbox);
  const internal = useInternalDashboard(!isSupplier);
  const supplier = useSupplierDashboard(isSupplier);
  return {
    inbox: inbox.data?.pending ?? 0,
    callOrders: isSupplier
      ? (supplier.data?.callOrdersAwaiting ?? 0)
      : (internal.data?.pendingCallOrders ?? 0),
    receipts: isSupplier
      ? (supplier.data?.receiptsWithoutInvoice ?? 0)
      : (internal.data?.warningReceipts ?? 0) + (internal.data?.invalidReceipts ?? 0),
  };
}

const badgeTitle: Record<NavBadge, string> = {
  inbox: 'tài liệu chờ tôi xử lý',
  callOrders: 'đơn gọi hàng chờ xác nhận',
  receipts: 'phiếu cần xử lý',
};

function toMenu(items: NavItem[], counts: Record<NavBadge, number>, collapsed: boolean): MenuProps['items'] {
  return items.map((i) => {
    const count = i.badge ? counts[i.badge] : 0;
    const urgent = i.badge === 'inbox';
    return {
      key: i.key,
      icon: i.icon ? (
        <span className="app-sider__icon">
          {i.icon}
          {collapsed && count > 0 && <span className="app-sider__dot" aria-hidden />}
        </span>
      ) : undefined,
      label: (
        <span className="app-sider__label">
          <span className="app-sider__text">{i.label}</span>
          {count > 0 && (
            <span
              className={`app-sider__badge${urgent ? ' app-sider__badge--urgent' : ''}`}
              aria-label={`${count} ${i.badge ? badgeTitle[i.badge] : ''}`}
            >
              {count > 99 ? '99+' : count}
            </span>
          )}
        </span>
      ),
      children: i.children ? toMenu(i.children, counts, collapsed) : undefined,
    };
  });
}

function initials(name: string | undefined) {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean);
  const last = parts.at(-1)?.[0] ?? '?';
  const previous = parts.length > 1 ? (parts.at(-2)?.[0] ?? '') : '';
  return (previous + last).toUpperCase();
}

/** Environment label: shown on development / local stacks, hidden in production. */
const environmentLabel = (() => {
  const configured = import.meta.env.VITE_ENV_LABEL as string | undefined;
  if (configured !== undefined) return configured || null;
  const host = window.location.hostname;
  return import.meta.env.DEV || host === 'localhost' || host === '127.0.0.1' ? 'Development' : null;
})();

/** Shared shell for both portals (design/App Shell.dc.html): navy sider, sticky header, content up to 1440px. */
export function AppShell({ brand, subtitle, nav }: { brand: string; subtitle: string; nav: NavGroup[] }) {
  const navigate = useNavigate();
  const location = useLocation();
  const { user, can, logout, isSupplier } = useAuth();
  const screens = Grid.useBreakpoint();
  const mobile = !screens.lg;
  const { mode, toggle } = useThemeMode();
  const [collapsed, setCollapsed] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [paletteOpen, setPaletteOpen] = useState(false);

  const groups = useMemo(
    () => nav.map((g) => ({ ...g, items: filterItems(g.items, can) })).filter((g) => g.items.length > 0),
    [nav, can],
  );
  const flat = useMemo(() => flattenNav(groups), [groups]);
  const counts = useNavBadges(isSupplier, can(P.SigningInbox));

  const openLink = (link: string | null) => {
    const target = portalLink(link, isSupplier);
    if (target) navigate(target);
  };
  useRealtime(!!user, openLink);

  const current = useMemo(
    () =>
      flat
        .filter((i) => location.pathname === i.key || location.pathname.startsWith(i.key + '/'))
        .sort((a, b) => b.key.length - a.key.length)[0],
    [flat, location.pathname],
  );
  const crumb = current ?? pageOutsideMenu(location.pathname);
  const selected = current ? [current.key] : [];
  const parentOfCurrent = groups
    .flatMap((g) => g.items)
    .filter((i) => i.children?.some((c) => c.key === current?.key))
    .map((i) => i.key);
  // Sub-menus opened by the user, plus always the one holding the current page.
  const [openKeys, setOpenKeys] = useState<string[]>([]);
  const menuOpenKeys = [...new Set([...openKeys, ...parentOfCurrent])];

  // Ctrl+K / ⌘K opens the command palette.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setPaletteOpen(true);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const expanded = mobile || !collapsed;
  const menuItems: MenuProps['items'] = groups.map((g) => ({
    type: 'group' as const,
    key: `group:${g.group}`,
    label: expanded ? g.group : undefined,
    children: toMenu(g.items, counts, !expanded),
  }));

  const orgLine = isSupplier ? (user?.supplierName ?? subtitle) : (user?.facilityName ?? subtitle);
  const siderContent = (
    <div className="app-sider__inner">
      <div className="app-sider__brand">
        <img src="/favicon.svg" alt="" width={32} height={32} />
        {expanded && (
          <div className="app-sider__brand-text">
            <span className="app-sider__brand-name">{brand}</span>
            <span className="app-sider__brand-org" title={orgLine}>
              {orgLine}
            </span>
          </div>
        )}
      </div>
      {isSupplier && expanded && (
        <div className="app-sider__portal">
          <Building2 size={14} strokeWidth={1.75} aria-hidden />
          Cổng Nhà cung cấp
        </div>
      )}
      <nav aria-label="Menu chính" className="app-sider__nav">
        <Menu
          theme="dark"
          mode="inline"
          inlineCollapsed={!expanded}
          selectedKeys={selected}
          openKeys={expanded ? menuOpenKeys : undefined}
          onOpenChange={setOpenKeys}
          items={menuItems}
          onClick={({ key }) => {
            navigate(key);
            setDrawerOpen(false);
          }}
        />
      </nav>
      {!mobile && (
        <div className="app-sider__footer">
          <Tooltip title={collapsed ? 'Mở rộng menu' : undefined} placement="right">
            <button
              type="button"
              className="app-sider__collapse"
              onClick={() => setCollapsed((c) => !c)}
              aria-label={collapsed ? 'Mở rộng menu' : 'Thu gọn menu'}
            >
              {collapsed ? (
                <ChevronsRight size={18} strokeWidth={1.75} aria-hidden />
              ) : (
                <>
                  <ChevronsLeft size={18} strokeWidth={1.75} aria-hidden />
                  <span>Thu gọn menu</span>
                </>
              )}
            </button>
          </Tooltip>
        </div>
      )}
    </div>
  );

  const accountPath = isSupplier ? '/ncc/tai-khoan' : '/tai-khoan';
  return (
    <Layout className="app-shell" style={{ minHeight: '100vh' }}>
      {!mobile && (
        <Layout.Sider
          className="app-sider"
          theme="dark"
          width={248}
          collapsedWidth={68}
          collapsed={collapsed}
          trigger={null}
          style={{ position: 'sticky', top: 0, height: '100vh' }}
        >
          {siderContent}
        </Layout.Sider>
      )}
      {mobile && (
        <Drawer
          placement="left"
          width={280}
          open={drawerOpen}
          onClose={() => setDrawerOpen(false)}
          closable={false}
          rootClassName="app-sider app-sider--drawer"
          styles={{ body: { padding: 0 } }}
        >
          {siderContent}
        </Drawer>
      )}
      <Layout style={{ minWidth: 0 }}>
        <Layout.Header className="app-header">
          {mobile && (
            <button
              type="button"
              className="app-header__icon-btn app-header__menu-btn"
              onClick={() => setDrawerOpen(true)}
              aria-label="Mở menu"
            >
              <MenuIcon size={18} strokeWidth={1.75} aria-hidden />
            </button>
          )}
          <nav aria-label="Breadcrumb" className="app-header__crumbs">
            <House size={14} strokeWidth={1.75} aria-hidden />
            {crumb && screens.md && (
              <>
                <ChevronRight size={14} strokeWidth={1.75} aria-hidden />
                <span className="app-header__crumb-group">{crumb.group}</span>
              </>
            )}
            <ChevronRight size={14} strokeWidth={1.75} aria-hidden />
            <span className="app-header__crumb-page">{crumb?.label ?? subtitle}</span>
          </nav>
          <div style={{ flex: 1 }} />
          {screens.md ? (
            <button type="button" className="app-header__search" onClick={() => setPaletteOpen(true)}>
              <Search size={16} strokeWidth={1.75} aria-hidden />
              <span className="app-header__search-text">Tìm hợp đồng, phiếu, nhà cung cấp…</span>
              <span className="app-header__kbd" aria-hidden>
                <kbd>Ctrl</kbd>
                <kbd>K</kbd>
              </span>
            </button>
          ) : (
            <button
              type="button"
              className="app-header__icon-btn"
              onClick={() => setPaletteOpen(true)}
              aria-label="Tìm kiếm"
            >
              <Search size={18} strokeWidth={1.75} aria-hidden />
            </button>
          )}
          {environmentLabel && screens.md && (
            <span className="app-header__env" title="Môi trường phát triển — ẩn ở production">
              <span className="app-header__env-dot" aria-hidden />
              {environmentLabel}
            </span>
          )}
          <NotificationBell onOpen={openLink} />
          <Tooltip title={mode === 'dark' ? 'Giao diện sáng' : 'Giao diện tối'}>
            <button
              type="button"
              className="app-header__icon-btn"
              onClick={toggle}
              aria-label="Đổi giao diện sáng / tối"
            >
              {mode === 'dark' ? (
                <Sun size={18} strokeWidth={1.75} aria-hidden />
              ) : (
                <Moon size={18} strokeWidth={1.75} aria-hidden />
              )}
            </button>
          </Tooltip>
          <span className="app-header__divider" aria-hidden />
          <Dropdown
            trigger={['click']}
            placement="bottomRight"
            popupRender={(menu) => (
              <div className="app-user-menu">
                <div className="app-user-menu__head">
                  <div className="app-user-menu__name">{user?.fullName}</div>
                  <div className="app-user-menu__unit">
                    {user?.supplierName ?? user?.departmentName ?? user?.jobTitle ?? ''}
                  </div>
                  <div className="app-user-menu__login mono">{user?.userName}</div>
                </div>
                {menu}
              </div>
            )}
            menu={{
              items: [
                {
                  key: 'account',
                  icon: <ShieldCheck size={16} strokeWidth={1.75} aria-hidden />,
                  label: 'Tài khoản & bảo mật',
                },
                { type: 'divider' },
                {
                  key: 'logout',
                  icon: <LogOut size={16} strokeWidth={1.75} aria-hidden />,
                  label: 'Đăng xuất',
                  danger: true,
                },
              ],
              onClick: async ({ key }) => {
                if (key === 'account') navigate(accountPath);
                if (key === 'logout') {
                  await logout();
                  navigate('/dang-nhap', { replace: true });
                }
              },
            }}
          >
            <button type="button" className="app-header__user" aria-label="Tài khoản">
              <span className="app-header__avatar" aria-hidden>
                {initials(user?.fullName)}
              </span>
              {screens.md && (
                <span className="app-header__user-text">
                  <span className="app-header__user-name">{user?.fullName}</span>
                  <span className="app-header__user-title">{user?.supplierName ?? user?.jobTitle ?? ''}</span>
                </span>
              )}
              <ChevronDown size={16} strokeWidth={1.75} aria-hidden />
            </button>
          </Dropdown>
        </Layout.Header>
        <Layout.Content className="app-content">
          <div className="app-content__inner">
            <Outlet />
          </div>
        </Layout.Content>
      </Layout>
      <CommandPalette
        open={paletteOpen}
        onClose={() => setPaletteOpen(false)}
        pages={flat}
        contractsPath={isSupplier ? '/ncc/hop-dong' : '/hop-dong'}
        onNavigate={(path) => {
          setPaletteOpen(false);
          navigate(path);
        }}
      />
    </Layout>
  );
}
