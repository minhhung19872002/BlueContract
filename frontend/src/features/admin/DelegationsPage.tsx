import {
  App,
  Button,
  Card,
  DatePicker,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Switch,
  Table,
  Typography,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { useState } from 'react';
import dayjs, { type Dayjs } from 'dayjs';
import { resources, useAction, useGet, useLookup, useSave, type DelegationDto } from '../../api/admin';
import { StatusTag } from '../../components/StatusTag';
import { PageHeader, useApiError } from '../../components/common';
import { useAuth } from '../../auth/useAuth';
import { P } from '../../auth/permissions';
import { formatDateTime } from '../../utils/format';

function delegationState(d: DelegationDto) {
  if (d.revokedAt) return 'revoked';
  if (d.isEffective) return 'effective';
  return dayjs(d.validFrom).isAfter(dayjs()) ? 'upcoming' : 'expired';
}

/** Ủy quyền ký — documents signed by the delegate show "KT. GIÁM ĐỐC / PHÓ GIÁM ĐỐC". */
export function DelegationsPage() {
  const [onlyEffective, setOnlyEffective] = useState(false);
  const list = useGet<DelegationDto[]>(`${resources.delegations}?onlyEffective=${onlyEffective}`, [
    resources.delegations,
    onlyEffective,
  ]);
  const users = useLookup('internal-users');
  const create = useSave<object>(resources.delegations);
  const revoke = useAction<string>(resources.delegations, (id) => ({
    url: `${resources.delegations}/${id}/revoke`,
  }));
  const [open, setOpen] = useState(false);
  const [form] = Form.useForm();
  const { message } = App.useApp();
  const onError = useApiError();
  const { user, can } = useAuth();
  const isAdmin = can(P.UserManage);
  const userOptions = (users.data ?? []).map((u) => ({ value: u.id, label: `${u.name} (${u.code})` }));

  return (
    <>
      <PageHeader
        title="Ủy quyền ký"
        subtitle="Người được ủy quyền ký thay trong khoảng thời gian; tài liệu thể hiện “KT. GIÁM ĐỐC / PHÓ GIÁM ĐỐC”."
        extra={
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => {
              form.resetFields();
              form.setFieldsValue({
                delegatorUserId: isAdmin ? undefined : user?.id,
                delegatorTitle: 'Giám đốc',
                delegateTitle: 'Phó Giám đốc',
              });
              setOpen(true);
            }}
          >
            Thêm ủy quyền
          </Button>
        }
      />
      <Card>
        <div style={{ marginBottom: 12 }}>
          <Switch checked={onlyEffective} onChange={setOnlyEffective} />{' '}
          <Typography.Text>Chỉ hiện ủy quyền đang hiệu lực</Typography.Text>
        </div>
        <Table<DelegationDto>
          rowKey="id"
          size="middle"
          loading={list.isLoading}
          dataSource={list.data ?? []}
          pagination={false}
          scroll={{ x: 'max-content' }}
          columns={[
            { title: 'Người ủy quyền', render: (_, d) => `${d.delegatorName} (${d.delegatorTitle})` },
            { title: 'Người được ủy quyền', render: (_, d) => `${d.delegateName} (${d.delegateTitle})` },
            { title: 'Từ', dataIndex: 'validFrom', render: (v: string) => formatDateTime(v) },
            { title: 'Đến', dataIndex: 'validTo', render: (v: string) => formatDateTime(v) },
            { title: 'Lý do', dataIndex: 'reason' },
            {
              title: 'Trạng thái',
              render: (_, d) => <StatusTag domain="delegation" value={delegationState(d)} />,
            },
            {
              title: '',
              render: (_, d) =>
                !d.revokedAt && dayjs(d.validTo).isAfter(dayjs()) ? (
                  <Popconfirm
                    title="Thu hồi ủy quyền này?"
                    okText="Thu hồi"
                    cancelText="Hủy"
                    onConfirm={() =>
                      revoke.mutateAsync(d.id).then(() => message.success('Đã thu hồi'), onError)
                    }
                  >
                    <Button size="small" danger>
                      Thu hồi
                    </Button>
                  </Popconfirm>
                ) : null,
            },
          ]}
        />
      </Card>
      <Modal
        title="Thêm ủy quyền ký"
        open={open}
        onCancel={() => setOpen(false)}
        onOk={() => form.submit()}
        okText="Lưu"
        cancelText="Hủy"
        destroyOnHidden
      >
        <Form
          form={form}
          layout="vertical"
          onFinish={async (v: {
            delegatorUserId: string;
            delegateUserId: string;
            delegatorTitle: string;
            delegateTitle: string;
            period: [Dayjs, Dayjs];
            reason?: string;
          }) => {
            try {
              await create.mutateAsync({
                body: {
                  delegatorUserId: v.delegatorUserId,
                  delegateUserId: v.delegateUserId,
                  delegatorTitle: v.delegatorTitle,
                  delegateTitle: v.delegateTitle,
                  validFrom: v.period[0].toISOString(),
                  validTo: v.period[1].toISOString(),
                  reason: v.reason,
                },
              });
              message.success('Đã thêm ủy quyền');
              setOpen(false);
            } catch (e) {
              onError(e);
            }
          }}
        >
          <Form.Item
            name="delegatorUserId"
            label="Người ủy quyền"
            rules={[{ required: true, message: 'Bắt buộc' }]}
          >
            <Select showSearch optionFilterProp="label" options={userOptions} disabled={!isAdmin} />
          </Form.Item>
          <Form.Item name="delegatorTitle" label="Chức danh người ủy quyền" rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item
            name="delegateUserId"
            label="Người được ủy quyền"
            rules={[{ required: true, message: 'Bắt buộc' }]}
          >
            <Select showSearch optionFilterProp="label" options={userOptions} />
          </Form.Item>
          <Form.Item name="delegateTitle" label="Chức danh người được ủy quyền" rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item
            name="period"
            label="Thời gian hiệu lực"
            rules={[{ required: true, message: 'Chọn thời gian' }]}
          >
            <DatePicker.RangePicker showTime format="DD/MM/YYYY HH:mm" style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="reason" label="Lý do">
            <Input.TextArea rows={2} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
