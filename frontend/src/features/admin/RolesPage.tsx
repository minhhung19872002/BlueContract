import { App, Button, Checkbox, Form, Input, Modal, Spin, Tooltip } from 'antd';
import { PlusOutlined, UndoOutlined } from '@ant-design/icons';
import { ChevronDown, ChevronRight, Search } from 'lucide-react';
import { Fragment, useMemo, useState } from 'react';
import { resources, useAction, useGet, useSave, type PermissionMatrixDto } from '../../api/admin';
import { PageHeader, useApiError } from '../../components/common';
import { omitKey } from '../../utils/format';
import './roles.css';

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
  const [search, setSearch] = useState('');
  const [closed, setClosed] = useState<Record<string, boolean>>({});
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

  const groups = useMemo(() => {
    const q = search.trim().toLowerCase();
    const perms = (matrix.data?.permissions ?? []).filter(
      (p) => !q || p.name.toLowerCase().includes(q) || p.code.toLowerCase().includes(q),
    );
    return [...new Set(perms.map((p) => p.group))].map((g) => ({
      name: g,
      perms: perms.filter((p) => p.group === g),
    }));
  }, [matrix.data, search]);

  const save = async (roleId: string, name: string) => {
    try {
      await savePermissions.mutateAsync({ id: roleId, permissions: [...current(roleId)] });
      setDraft((prev) => omitKey(prev, roleId));
      message.success(`Đã lưu quyền cho ${name}`);
    } catch (e) {
      onError(e);
    }
  };

  return (
    <>
      <PageHeader
        title="Vai trò & phân quyền"
        subtitle="Ma trận quyền theo phân hệ — mỗi cột lưu riêng khi có thay đổi; người dùng nhận quyền mới ngay ở lần gọi tiếp theo"
        extra={
          <>
            <Input
              allowClear
              className="roles__search"
              prefix={<Search size={16} aria-hidden />}
              placeholder="Tìm quyền"
              aria-label="Tìm quyền"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
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
          </>
        }
      />
      <section className="roles">
        {matrix.isLoading ? (
          <Spin style={{ margin: 48 }} />
        ) : (
          <div className="roles__scroll">
            <table className="roles__table">
              <thead>
                <tr>
                  <th className="roles__corner">Quyền</th>
                  {roles.map((role) => (
                    <th
                      key={role.id}
                      className={`roles__role${dirty(role.id) ? ' is-dirty' : ''}`}
                      title={role.description ?? undefined}
                    >
                      <div className="roles__role-name">{role.name}</div>
                      <div className="roles__role-code mono">{role.code}</div>
                      <div className="roles__role-users">{role.userCount} người</div>
                      {dirty(role.id) && (
                        <div className="roles__role-actions">
                          <Button
                            size="small"
                            type="primary"
                            loading={savePermissions.isPending && savePermissions.variables?.id === role.id}
                            onClick={() => void save(role.id, role.name)}
                          >
                            Lưu
                          </Button>
                          <Tooltip title="Hoàn tác">
                            <Button
                              size="small"
                              icon={<UndoOutlined />}
                              aria-label={`Hoàn tác ${role.name}`}
                              onClick={() => setDraft((prev) => omitKey(prev, role.id))}
                            />
                          </Tooltip>
                        </div>
                      )}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {groups.length === 0 && (
                  <tr>
                    <td className="roles__empty" colSpan={roles.length + 1}>
                      Không có quyền phù hợp
                    </td>
                  </tr>
                )}
                {groups.map((g) => {
                  const open = !closed[g.name] || !!search.trim();
                  return (
                    <Fragment key={g.name}>
                      <tr className="roles__group">
                        <td>
                          <button
                            type="button"
                            className="roles__group-toggle"
                            aria-expanded={open}
                            onClick={() => setClosed((c) => ({ ...c, [g.name]: open }))}
                          >
                            {open ? (
                              <ChevronDown size={15} aria-hidden />
                            ) : (
                              <ChevronRight size={15} aria-hidden />
                            )}
                            {g.name}
                            <span className="roles__group-count">{g.perms.length} quyền</span>
                          </button>
                        </td>
                        <td colSpan={roles.length} />
                      </tr>
                      {open &&
                        g.perms.map((p) => (
                          <tr key={p.code} className="roles__perm">
                            <td>
                              <div className="roles__perm-name">{p.name}</div>
                              <div className="roles__perm-code mono">{p.code}</div>
                            </td>
                            {roles.map((role) => (
                              <td key={role.id} className={dirty(role.id) ? 'is-dirty' : undefined}>
                                <Checkbox
                                  aria-label={`${role.name} — ${p.name}`}
                                  checked={current(role.id).has(p.code)}
                                  onChange={(e) => toggle(role.id, p.code, e.target.checked)}
                                />
                              </td>
                            ))}
                          </tr>
                        ))}
                    </Fragment>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>
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
