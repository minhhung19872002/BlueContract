import { candidateLabel, filterCandidates } from './candidateFilter';
import { foldVietnamese } from '../../utils/search';

const row = (contractItemId: string, itemName: string, contractNumber: string, supplierName: string) =>
  ({ contractItemId, itemName, contractNumber, supplierName }) as Parameters<typeof candidateLabel>[0];

const rows = [
  row('a', 'Amoxicillin 500mg', '12/2026/HĐ-BV', 'Công ty Dược phẩm Minh An'),
  row('b', 'Paracetamol 500mg', '12/2026/HĐ-BV', 'Công ty Dược phẩm Minh An'),
  row('c', 'Găng tay y tế', '07/2026/HĐ-VT', 'Công ty Thiết bị Đông Á'),
];

describe('filterCandidates', () => {
  it('finds items by name, contract number or supplier, without accents or case', () => {
    expect(filterCandidates(rows, 'amox', false, {}).map((r) => r.contractItemId)).toEqual(['a']);
    expect(filterCandidates(rows, 'GANG TAY', false, {}).map((r) => r.contractItemId)).toEqual(['c']);
    expect(filterCandidates(rows, 'dong a', false, {}).map((r) => r.contractItemId)).toEqual(['c']);
    expect(filterCandidates(rows, '12/2026', false, {}).map((r) => r.contractItemId)).toEqual(['a', 'b']);
    expect(filterCandidates(rows, '  ', false, {})).toHaveLength(3);
  });

  it('can show only the items that already have a quantity', () => {
    const entries = { b: { qty: 10 }, c: { qty: 0 } };
    expect(filterCandidates(rows, '', true, entries).map((r) => r.contractItemId)).toEqual(['b']);
    expect(filterCandidates(rows, 'amox', true, entries)).toHaveLength(0);
  });
});

describe('candidateLabel', () => {
  it('names the item and its contract, so every quantity box has its own label', () => {
    expect(candidateLabel(rows[0]!)).toBe('Amoxicillin 500mg — HĐ 12/2026/HĐ-BV');
  });
});

describe('foldVietnamese', () => {
  it('drops accents and case', () => {
    expect(foldVietnamese('Hợp ĐỒNG Đông Á')).toBe('hop dong dong a');
  });
});
