import { describe, expect, it } from 'vitest';
import { describeAuditDetails } from './auditDetails';

describe('describeAuditDetails', () => {
  it('translates enum numbers and hides ids', () => {
    expect(describeAuditDetails('Contract', '{"Status": 2}')).toEqual(['Trạng thái: NCC rà soát']);
    expect(describeAuditDetails('Document', '{"id": "01a1", "code": "WF_CONTRACT"}')).toEqual([
      'Luồng: WF_CONTRACT',
    ]);
  });

  it('formats money and keeps only known fields', () => {
    expect(
      describeAuditDetails(
        'Contract',
        '{"number": "15/2026/HĐ", "totalAmount": 2960206800, "procurementType": 0}',
      ),
    ).toEqual(['Số: 15/2026/HĐ', 'Tổng giá trị: 2.960.206.800 ₫']);
  });

  it('never returns raw JSON objects', () => {
    expect(describeAuditDetails('Contract', '{"Items": [{"a": 1}], "SupplierId": "x"}')).toEqual([]);
    expect(describeAuditDetails('Contract', null)).toEqual([]);
  });
});
