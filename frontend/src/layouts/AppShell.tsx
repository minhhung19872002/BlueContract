import { Avatar, Dropdown, Grid, Layout, Menu, Space, Typography, type MenuProps } from 'antd';
import { LogoutOutlined, UserOutlined, SafetyOutlined } from '@ant-design/icons';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useMemo, useState, type ReactNode } from 'react';
import { useAuth } from '../auth/useAuth';

export interface NavItem {
  key: string;
  label: string;
  icon?: ReactNode;
  /** Visible if the user has ANY of these permissions (omit = always). */
  permissions?: string[];
  children?: NavItem[];
}

function filterItems(items: NavItem[], can: (...p: string[]) => boolean): NavItem[] {
  return items
    .filter((i) => !i.permissions || can(...i.permissions))
    .map((i) => (i.children ? { ...i, children: filterItems(i.children, can) } : i))
    .filter((i) => !i.children || i.children.length > 0);
}

function toMenu(items: NavItem[]): MenuProps['items'] {
  return items.map((i) => ({
    key: i.key,
    icon: i.icon,
    label: i.label,
    children: i.children ? toMenu(i.children) : undefined,
  }));
}

/** Shared shell for both portals: collapsible sider, permission-filtered menu, account dropdown. */
export function AppShell({ brand, subtitle, nav }: { brand: string; subtitle: string; nav: NavItem[] }) {
  const navigate = useNavigate();
  const location = useLocation();
  const { user, can, logout, isSupplier } = useAuth();
  const screens = Grid.useBreakpoint();
  const [collapsed, setCollapsed] = useState(false);

  const items = useMemo(() => filterItems(nav, can), [nav, can]);
  const selected = useMemo(() => {
    const flat = items.flatMap((i) => [i, ...(i.children ?? [])]);
    const match = flat
      .filter((i) => location.pathname.startsWith(i.key))
      .sort((a, b) => b.key.length - a.key.length)[0];
    return match ? [match.key] : [];
  }, [items, location.pathname]);
  const openKeys = items.filter((i) => i.children?.some((c) => selected.includes(c.key))).map((i) => i.key);

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Layout.Sider
        breakpoint="lg"
        collapsedWidth={screens.xs ? 0 : 64}
        collapsible
        collapsed={collapsed}
        onCollapse={setCollapsed}
        theme="light"
        width={248}
        style={{ borderRight: '1px solid #f0f0f0' }}
      >
        <div
          style={{
            padding: '16px 20px',
            fontWeight: 700,
            fontSize: 18,
            color: '#1554ad',
            whiteSpace: 'nowrap',
            overflow: 'hidden',
          }}
        >
          {collapsed ? 'BP' : brand}
        </div>
        <Menu
          mode="inline"
          selectedKeys={selected}
          defaultOpenKeys={openKeys}
          items={toMenu(items)}
          onClick={({ key }) => {
            navigate(key);
            if (screens.xs) setCollapsed(true);
          }}
          style={{ borderInlineEnd: 'none' }}
        />
      </Layout.Sider>
      <Layout>
        <Layout.Header
          style={{
            background: '#fff',
            paddingInline: 16,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 12,
            borderBottom: '1px solid #f0f0f0',
          }}
        >
          <Typography.Text strong ellipsis style={{ minWidth: 0 }}>
            {subtitle}
          </Typography.Text>
          <Dropdown
            menu={{
              items: [
                { key: 'account', icon: <SafetyOutlined />, label: 'Tài khoản & bảo mật' },
                { type: 'divider' },
                { key: 'logout', icon: <LogoutOutlined />, label: 'Đăng xuất', danger: true },
              ],
              onClick: async ({ key }) => {
                if (key === 'account') navigate(isSupplier ? '/ncc/tai-khoan' : '/tai-khoan');
                if (key === 'logout') {
                  await logout();
                  navigate('/dang-nhap', { replace: true });
                }
              },
            }}
          >
            <Space style={{ cursor: 'pointer' }}>
              <Avatar size="small" icon={<UserOutlined />} style={{ background: '#1554ad' }} />
              {screens.md && (
                <span>
                  {user?.fullName}
                  <Typography.Text type="secondary" style={{ marginLeft: 6 }}>
                    {user?.supplierName ?? user?.jobTitle ?? ''}
                  </Typography.Text>
                </span>
              )}
            </Space>
          </Dropdown>
        </Layout.Header>
        <Layout.Content style={{ padding: screens.xs ? 12 : 20 }}>
          <Outlet />
        </Layout.Content>
      </Layout>
    </Layout>
  );
}
