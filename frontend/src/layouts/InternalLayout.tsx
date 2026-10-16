import { Layout, Menu, Typography } from 'antd';
import { DashboardOutlined } from '@ant-design/icons';
import { Outlet, useNavigate } from 'react-router-dom';
import { useState } from 'react';

const { Header, Sider, Content } = Layout;

/** Shell for hospital staff. Menu items are added per phase and filtered by permission from Phase 1. */
export function InternalLayout() {
  const navigate = useNavigate();
  const [collapsed, setCollapsed] = useState(false);

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider breakpoint="lg" collapsible collapsed={collapsed} onCollapse={setCollapsed} theme="light">
        <div
          style={{ padding: 16, fontWeight: 700, color: '#1554ad', whiteSpace: 'nowrap', overflow: 'hidden' }}
        >
          {collapsed ? 'BP' : 'BlueProcure'}
        </div>
        <Menu
          mode="inline"
          defaultSelectedKeys={['/']}
          onClick={({ key }) => navigate(key)}
          items={[{ key: '/', icon: <DashboardOutlined />, label: 'Tổng quan' }]}
        />
      </Sider>
      <Layout>
        <Header style={{ background: '#fff', paddingInline: 16, display: 'flex', alignItems: 'center' }}>
          <Typography.Text strong>Cổng Quản lý &amp; Thanh toán Mua sắm Hàng hóa Trực tuyến</Typography.Text>
        </Header>
        <Content style={{ padding: 16 }}>
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  );
}
