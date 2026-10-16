import {
  App,
  Button,
  Card,
  Checkbox,
  Form,
  Input,
  Modal,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import { PlusOutlined, SaveOutlined, UndoOutlined } from '@ant-design/icons';
import { useMemo, useState } from 'react';
import { resources, useAction, useGet, useSave, type PermissionMatrixDto } from '../../api/admin';
import { PageHeader, useApiError } from '../../components/common';
import { omitKey } from '../../utils/format';

/** Ma trận phân quyền Role × Permission with per-role save. */
export function RolesPage() {
  const matrix = useGet<PermissionMatrixDto>(`${resources.roles}/matrix`, [resources.roles, 'matrix']);
  const [draft, setDraft] = useState<Record<string, Set<string>>>({});
  const savePermissions = useAction<{ id: string; permissions: string[] }>(
    resources.roles,
    ({ id, permissions }) => ({
      url: `${resources.roles}/${id}/permissions`,
      method: 'put',
      body: { permissions },
    }),
  );
  const createRole = useSave<object>(resources.roles);
  const [creating, setCreating] = useState(false);
  const [form] = Form.useForm();
  const { message } = App.useApp();
  const onError = useApiError();

  const roles = useMemo(() => matrix.data?.roles ?? [], [matrix.data]);
  const current = (roleId: string) =>
    draft[roleId] ?? new Set(roles.find((r) => r.id === roleId)?.permissions ?? []);
  const dirty = (roleId: string) => {
    const original = new Set(roles.find((r) => r.id === roleId)?.permissions ?? []);
    const d = draft[roleId];
    return !!d && (d.size !== original.size || [...d].some((p) => !original.has(p)));
  };

  const toggle = (roleId: string, code: string, checked: boolean) =>
    setDraft((prev) => {
      const next = new Set(prev[roleId] ?? current(roleId));
      if (checked) next.add(code);
      else next.delete(code);
      return { ...prev, [roleId]: next };
    });

  const rows = useMemo(() => {
    const perms = matrix.data?.permissions ?? [];
    const groups = [...new Set(perms.map((p) => p.group))];
    return groups.map((g) => ({
      key: g,
      name: g,
      isGroup: true,
      children: perms
        .filter((p) => p.group === g)
        .map((p) => ({ key: p.code, name: p.name, code: p.code, isGroup: false })),
    }));
  }, [matrix.data]);

  type Row = { key: string; name: string; code?: string; isGroup: boolean };

  return (
    <>
      <PageHeader
        title="Vai trò & phân quyền"
        subtitle="Đánh dấu quyền cho từng vai trò rồi bấm Lưu ở cột tương ứng. Người dùng nhận quyền mới ngay ở lần gọi tiếp theo."
        extra={
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => {
              form.resetFields();
              setCreating(true);
            }}
          >
            Thêm vai trò
          </Button>
        }
      />
      <Card styles={{ body: { padding: 0 } }}>
        <Table<Row>
          // Remount once permissions load so every group starts expanded.
          key={rows.length}
          size="small"
          loading={matrix.isLoading}
          pagination={false}
          sticky
          scroll={{ x: 'max-content' }}
          dataSource={rows}
          expandable={{ defaultExpandAllRows: true }}
          columns={[
            {
              title: 'Quyền',
              dataIndex: 'name',
              fixed: 'left',
              width: 320,
              render: (v: string, r) =>
                r.isGroup ? (
                  <Typography.Text strong>{v}</Typography.Text>
                ) : (
                  <Tooltip title={r.code}>
                    <span>{v}</span>
                  </Tooltip>
                ),
            },
            ...roles.map((role) => ({
              key: role.id,
              width: 120,
              align: 'center' as const,
              title: (
                <Space direction="vertical" size={2} style={{ alignItems: 'center' }}>
                  <Tooltip title={role.description}>
                    <Typography.Text strong style={{ whiteSpace: 'nowrap' }}>
                      {role.name}
                    </Typography.Text>
                  </Tooltip>
                  <Tag style={{ marginInlineEnd: 0 }}>{role.code}</Tag>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {role.userCount} người
                  </Typography.Text>
                  {dirty(role.id) && (
                    <Space size={4}>
                      <Button
                        size="small"
                        type="primary"
                        icon={<SaveOutlined />}
                        loading={savePermissions.isPending}
                        onClick={async () => {
                          try {
                            await savePermissions.mutateAsync({
                              id: role.id,
                              permissions: [...current(role.id)],
                            });
                            setDraft((prev) => omitKey(prev, role.id));
                            message.success(`Đã lưu quyền cho ${role.name}`);
                          } catch (e) {
                            onError(e);
                          }
                        }}
                      />
                      <Button
                        size="small"
                        icon={<UndoOutlined />}
                        onClick={() => setDraft((prev) => omitKey(prev, role.id))}
                      />
                    </Space>
                  )}
                </Space>
              ),
              render: (_: unknown, r: Row) =>
                r.isGroup ? null : (
                  <Checkbox
                    checked={current(role.id).has(r.code!)}
                    onChange={(e) => toggle(role.id, r.code!, e.target.checked)}
                  />
                ),
            })),
          ]}
        />
      </Card>
      <Modal
        title="Thêm vai trò"
        open={creating}
        onCancel={() => setCreating(false)}
        onOk={() => form.submit()}
        okText="Tạo"
        cancelText="Hủy"
        destroyOnHidden
      >
        <Form
          form={form}
          layout="vertical"
          onFinish={async (values) => {
            try {
              await createRole.mutateAsync({ body: values });
              message.success('Đã tạo vai trò');
              setCreating(false);
            } catch (e) {
              onError(e);
            }
          }}
        >
          <Form.Item
            name="code"
            label="Mã vai trò"
            rules={[
              {
                required: true,
                pattern: /^[A-Za-z][A-Za-z0-9_]{1,49}$/,
                message: 'Chữ cái đầu, chỉ gồm chữ, số, gạch dưới',
              },
            ]}
          >
            <Input placeholder="VD: PHO_GIAM_DOC" />
          </Form.Item>
          <Form.Item name="name" label="Tên vai trò" rules={[{ required: true, message: 'Bắt buộc' }]}>
            <Input />
          </Form.Item>
          <Form.Item name="description" label="Mô tả">
            <Input.TextArea rows={2} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
