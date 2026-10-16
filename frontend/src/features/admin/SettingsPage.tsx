import { App, Button, Card, Input, InputNumber, Space, Switch, Table, Typography } from 'antd';
import { SaveOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { resources, useAction, useGet, type SystemSettingDto } from '../../api/admin';
import { PageHeader, useApiError } from '../../components/common';
import { formatQuantity, omitKey } from '../../utils/format';

/** Tham số hệ thống: every threshold in spec §11 is editable here. */
export function SettingsPage() {
  const list = useGet<SystemSettingDto[]>(resources.settings, [resources.settings]);
  const [values, setValues] = useState<Record<string, string>>({});
  const save = useAction<{ key: string; value: string }>(resources.settings, ({ key, value }) => ({
    url: `${resources.settings}/${encodeURIComponent(key)}`,
    method: 'put',
    body: { value },
  }));
  const { message } = App.useApp();
  const onError = useApiError();

  const editor = (s: SystemSettingDto) => {
    const value = values[s.key] ?? s.value;
    const set = (v: string) => setValues((prev) => ({ ...prev, [s.key]: v }));
    switch (s.valueType) {
      case 'WholeNumber':
        return (
          <InputNumber
            value={Number(value)}
            precision={0}
            min={s.min ?? undefined}
            max={s.max ?? undefined}
            onChange={(v) => set(String(v ?? 0))}
            style={{ width: 180 }}
          />
        );
      case 'DecimalNumber':
        return (
          <InputNumber
            value={Number(value)}
            min={s.min ?? undefined}
            max={s.max ?? undefined}
            onChange={(v) => set(String(v ?? 0))}
            style={{ width: 180 }}
            decimalSeparator=","
          />
        );
      case 'YesNo':
        return <Switch checked={value === 'true'} onChange={(v) => set(String(v))} />;
      default:
        return <Input value={value} onChange={(e) => set(e.target.value)} style={{ width: 320 }} />;
    }
  };

  return (
    <>
      <PageHeader title="Tham số hệ thống" subtitle="Các ngưỡng nghiệp vụ dùng chung" />
      <Card>
        <Table<SystemSettingDto>
          rowKey="key"
          loading={list.isLoading}
          dataSource={list.data ?? []}
          pagination={false}
          scroll={{ x: 'max-content' }}
          columns={[
            {
              title: 'Tham số',
              dataIndex: 'description',
              render: (v: string, s) => (
                <Space direction="vertical" size={0}>
                  <span>{v}</span>
                  <Typography.Text type="secondary" code style={{ fontSize: 12 }}>
                    {s.key}
                  </Typography.Text>
                  {s.min !== null && s.max !== null && (
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      Từ {formatQuantity(s.min)} đến {formatQuantity(s.max)}
                    </Typography.Text>
                  )}
                </Space>
              ),
            },
            { title: 'Giá trị', key: 'value', width: 360, render: (_, s) => editor(s) },
            {
              title: '',
              key: 'save',
              width: 80,
              render: (_, s) =>
                values[s.key] !== undefined && values[s.key] !== s.value ? (
                  <Button
                    type="primary"
                    size="small"
                    icon={<SaveOutlined />}
                    onClick={async () => {
                      try {
                        await save.mutateAsync({ key: s.key, value: values[s.key]! });
                        setValues((prev) => omitKey(prev, s.key));
                        message.success('Đã lưu tham số');
                      } catch (e) {
                        onError(e);
                      }
                    }}
                  />
                ) : null,
            },
          ]}
        />
      </Card>
    </>
  );
}
