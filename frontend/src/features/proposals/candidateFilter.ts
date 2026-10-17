import type { ProposalCandidateDto } from '../../api/proposals';
import { matchesSearch } from '../../utils/search';

type Candidate = Pick<
  ProposalCandidateDto,
  'contractItemId' | 'itemName' | 'contractNumber' | 'supplierName'
>;

/** Rows of the proposal editor matching the search box, optionally only those with a quantity entered. */
export function filterCandidates<T extends Candidate>(
  rows: T[],
  query: string,
  onlyChosen: boolean,
  entries: Record<string, { qty?: number } | undefined>,
) {
  return rows.filter(
    (r) =>
      (!onlyChosen || (entries[r.contractItemId]?.qty ?? 0) > 0) &&
      matchesSearch(query, r.itemName, r.contractNumber, r.supplierName),
  );
}

/** Accessible name of a row's inputs: the item and its contract (the same item can come from several contracts). */
export const candidateLabel = (r: Candidate) => `${r.itemName} — HĐ ${r.contractNumber}`;
