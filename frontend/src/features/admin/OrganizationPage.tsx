import { App, Button, Card, Col, Form, Input, Row } from 'antd';
import { useEffect } from 'react';
import { resources, useAction, useGet, type OrganizationDto } from '../../api/admin';
import { PageHeader, useApiError } from '../../components/common';

/** Thông tin bệnh viện — used in every contract/document template; never hard-coded (spec §1). */
export function OrganizationPage() {
  const org = useGet<OrganizationDto>(resources.organization, [resources.organization]);
  const save = useAction<object>(resources.organization, (body) => ({
    url: resources.organization,
    method: 'put',
    body,
  }));
  const [form] = Form.useForm();
  const { message } = App.useApp();
  const onError = useApiError();

  useEffect(() => {
    if (org.data) form.setFieldsValue(org.data);
  }, [org.data, form]);

  return (
    <>
      <PageHeader
        title="Thông tin bệnh viện"
        subtitle="Dùng làm thông tin bên mua trong hợp đồng, biên bản và đề nghị thanh toán"
      />
      <Card loading={org.isLoading}>
        <Form
          form={form}
          layout="vertical"
          onFinish={async (values) => {
            try {
              await save.mutateAsync(values);
              message.success('Đã lưu thông tin bệnh viện');
            } catch (e) {
              onError(e);
            }
          }}
        >
          <Row gutter={16}>
            <Col xs={24} md={16}>
              <Form.Item name="name" label="Tên đơn vị" rules={[{ required: true, message: 'Bắt buộc' }]}>
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="shortName" label="Tên viết tắt">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item
                name="taxCode"
                label="Mã số thuế / mã ĐVQHNS"
                rules={[{ required: true, message: 'Bắt buộc' }]}
              >
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={16}>
              <Form.Item name="address" label="Địa chỉ">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="phone" label="Điện thoại">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item
                name="email"
                label="Email"
                rules={[{ type: 'email', message: 'Email không hợp lệ' }]}
              >
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8} />
            <Col xs={24} md={8}>
              <Form.Item name="representativeName" label="Người đại diện">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="representativeTitle" label="Chức vụ">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8} />
            <Col xs={24} md={8}>
              <Form.Item name="bankAccountNumber" label="Số tài khoản">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={16}>
              <Form.Item name="bankName" label="Mở tại">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="treasuryAccountNumber" label="Tài khoản Kho bạc">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={16}>
              <Form.Item name="treasuryName" label="Kho bạc Nhà nước">
                <Input />
              </Form.Item>
            </Col>
          </Row>
          <Button type="primary" htmlType="submit" loading={save.isPending}>
            Lưu
          </Button>
        </Form>
      </Card>
    </>
  );
}
