import dayjs from '../../i18n/dayjs';
import { contractKpis } from './contractKpis';

type Input = Parameters<typeof contractKpis>[0];

const contract = (patch: Partial<Input>): Input => ({
  status: 'Effective',
  totalAmount: 63_000_000,
  expiryDate: '2027-01-16',
  requiresWarehouseReceipt: true,
  items: [
    { amount: 63_000_000, unitPrice: 63_000_000, receivedQuantity: 1, paidQuantity: 1 },
    { amount: 0, unitPrice: 0, receivedQuantity: 0, paidQuantity: 0 },
  ],
  ...patch,
});

const today = dayjs('2026-10-17');

describe('contractKpis', () => {
  it('counts the days left and warns under 30 days while the contract is in force', () => {
    const [, , , expiry] = contractKpis(contract({}), today);
    expect(expiry!.hint).toBe('Còn 91 ngày');
    expect(expiry!.tone).toBeUndefined();
    const soon = contractKpis(contract({ expiryDate: '2026-11-01' }), today)[3]!;
    expect(soon.hint).toBe('Còn 15 ngày');
    expect(soon.tone).toBe('danger');
  });

  it('does not count down nor warn once the contract is liquidated or cancelled', () => {
    const liquidated = contractKpis(contract({ status: 'Liquidated', expiryDate: '2026-11-01' }), today)[3]!;
    expect(liquidated.hint).toBe('Đã thanh lý');
    expect(liquidated.tone).toBeUndefined();
    expect(contractKpis(contract({ status: 'Cancelled' }), today)[3]!.hint).toBe('Đã hủy');
  });

  it('calls the delivered value "nghiệm thu khối lượng" for a contract without warehouse receipts', () => {
    expect(contractKpis(contract({}), today)[1]!.label).toBe('Đã nhập kho');
    const service = contractKpis(contract({ requiresWarehouseReceipt: false }), today)[1]!;
    expect(service.label).toBe('Đã xác nhận khối lượng');
    expect(service.hint).toBe('100% giá trị hợp đồng');
  });
});
