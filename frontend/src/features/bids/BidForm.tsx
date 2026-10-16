import { Col, DatePicker, Form, Input, Row, Select, type FormInstance } from 'antd';
import { useLookup } from '../../api/admin';
import { useItemCategories } from '../../api/procurement';
import type { BidFormValues } from './bidFormValues';

const METHODS = [
  'Đấu thầu rộng rãi qua mạng',
  'Đấu thầu hạn chế',
  'Chào hàng cạnh tranh',
  'Chỉ định thầu',
  'Chỉ định thầu rút gọn',
  'Mua sắm trực tiếp',
  'Mua sắm tập trung',
];

/** Header of a bid decision (shared by create and edit). */
export function BidForm({ form, lockNumber }: { form: FormInstance<BidFormValues>; lockNumber?: boolean }) {
  const categories = useItemCategories();
  const facilities = useLookup('facilities');
  return (
    <Form form={form} layout="vertical">
      <Row gutter={12}>
        <Col xs={24} md={8}>
          <Form.Item
            name="number"
            label="Số quyết định"
            rules={[{ required: true, whitespace: true }]}
            extra="Khóa liên kết với HIS cùng MST nhà cung cấp"
          >
            <Input disabled={lockNumber} placeholder="VD: 125/QĐ-BVĐK" />
          </Form.Item>
        </Col>
        <Col xs={12} md={8}>
          <Form.Item name="signedDate" label="Ngày ký" rules={[{ required: true }]}>
            <DatePicker format="DD/MM/YYYY" style={{ width: '100%' }} />
          </Form.Item>
        </Col>
        <Col xs={12} md={8}>
          <Form.Item name="categoryId" label="Hạng mục" rules={[{ required: true }]}>
            <Select options={(categories.data ?? []).map((c) => ({ value: c.id, label: c.name }))} />
          </Form.Item>
        </Col>
        <Col span={24}>
          <Form.Item name="packageName" label="Tên gói thầu" rules={[{ required: true, whitespace: true }]}>
            <Input />
          </Form.Item>
        </Col>
        <Col span={24}>
          <Form.Item name="summary" label="Trích yếu" rules={[{ required: true, whitespace: true }]}>
            <Input.TextArea rows={2} />
          </Form.Item>
        </Col>
        <Col xs={24} md={8}>
          <Form.Item name="selectionMethod" label="Hình thức lựa chọn nhà thầu">
            <Select allowClear showSearch options={METHODS.map((m) => ({ value: m, label: m }))} />
          </Form.Item>
        </Col>
        <Col xs={24} md={8}>
          <Form.Item name="fundingSource" label="Nguồn vốn">
            <Input />
          </Form.Item>
        </Col>
        <Col xs={24} md={8}>
          <Form.Item name="executionPeriod" label="Thời gian thực hiện hợp đồng">
            <Input placeholder="VD: 12 tháng" />
          </Form.Item>
        </Col>
        <Col xs={12} md={8}>
          <Form.Item name="facilityId" label="Cơ sở">
            <Select
              allowClear
              placeholder="Toàn bệnh viện"
              options={(facilities.data ?? []).map((f) => ({ value: f.id, label: f.name }))}
            />
          </Form.Item>
        </Col>
        <Col xs={12} md={8}>
          <Form.Item name="validUntil" label="Hiệu lực đến">
            <DatePicker format="DD/MM/YYYY" style={{ width: '100%' }} />
          </Form.Item>
        </Col>
      </Row>
    </Form>
  );
}
