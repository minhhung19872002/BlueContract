import type { Dayjs } from 'dayjs';
import type { ContractDto } from '../../api/procurement';
import type { DocKpi } from '../../components/layout';
import dayjs from '../../i18n/dayjs';
import { formatDate, formatMoney } from '../../utils/format';

type KpiContract = Pick<ContractDto, 'status' | 'totalAmount' | 'expiryDate' | 'requiresWarehouseReceipt'> & {
  items: Pick<ContractDto['items'][number], 'amount' | 'unitPrice' | 'receivedQuantity' | 'paidQuantity'>[];
};

const pct = (part: number, whole: number) => (whole > 0 ? Math.round((part * 1000) / whole) / 10 : 0);

/** A closed contract no longer runs out: its validity card says how it ended instead of counting down. */
const CLOSED: Partial<Record<ContractDto['status'], string>> = {
  Liquidated: 'Đã thanh lý',
  Cancelled: 'Đã hủy',
};

/** The four cards at the top of the contract page. */
export function contractKpis(c: KpiContract, today: Dayjs = dayjs()): DocKpi[] {
  const totals = c.items.reduce(
    (s, i) => ({
      value: s.value + i.amount,
      received: s.received + i.receivedQuantity * i.unitPrice,
      paid: s.paid + i.paidQuantity * i.unitPrice,
    }),
    { value: 0, received: 0, paid: 0 },
  );
  const closed = CLOSED[c.status];
  const daysLeft = c.expiryDate ? dayjs(c.expiryDate).diff(today.startOf('day'), 'day') : null;
  return [
    { label: 'Tổng giá trị', value: formatMoney(c.totalAmount), hint: `${c.items.length} mặt hàng` },
    {
      // Service / repair contracts are delivered through a volume confirmation, not a warehouse receipt.
      label: c.requiresWarehouseReceipt ? 'Đã nhập kho' : 'Đã xác nhận khối lượng',
      value: formatMoney(totals.received),
      percent: pct(totals.received, totals.value),
      tone: 'teal',
      hint: `${pct(totals.received, totals.value)}% giá trị hợp đồng`,
    },
    {
      label: 'Đã thanh toán',
      value: formatMoney(totals.paid),
      percent: pct(totals.paid, totals.value),
      tone: 'success',
      hint: `${pct(totals.paid, totals.value)}% giá trị hợp đồng`,
    },
    {
      label: 'Hiệu lực đến',
      value: formatDate(c.expiryDate) || '—',
      hint:
        closed ??
        (daysLeft === null ? 'Chưa xác định' : daysLeft < 0 ? 'Đã hết hạn' : `Còn ${daysLeft} ngày`),
      tone: !closed && daysLeft !== null && daysLeft < 30 ? 'danger' : undefined,
    },
  ];
}
