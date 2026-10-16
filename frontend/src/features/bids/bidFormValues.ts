import dayjs from '../../i18n/dayjs';
import type { BidDecisionDto, SaveBidDecisionRequest } from '../../api/procurement';

export interface BidFormValues extends Omit<SaveBidDecisionRequest, 'signedDate' | 'validUntil'> {
  signedDate: dayjs.Dayjs;
  validUntil?: dayjs.Dayjs | null;
}

export const toBidRequest = (v: BidFormValues): SaveBidDecisionRequest => ({
  ...v,
  signedDate: v.signedDate.format('YYYY-MM-DD'),
  validUntil: v.validUntil ? v.validUntil.format('YYYY-MM-DD') : null,
  selectionMethod: v.selectionMethod ?? null,
  fundingSource: v.fundingSource ?? null,
  executionPeriod: v.executionPeriod ?? null,
  facilityId: v.facilityId ?? null,
});

export const fromBid = (b: BidDecisionDto): BidFormValues => ({
  number: b.number,
  signedDate: dayjs(b.signedDate),
  summary: b.summary,
  packageName: b.packageName,
  selectionMethod: b.selectionMethod,
  fundingSource: b.fundingSource,
  executionPeriod: b.executionPeriod,
  categoryId: b.categoryId,
  facilityId: b.facilityId,
  validUntil: b.validUntil ? dayjs(b.validUntil) : null,
});
