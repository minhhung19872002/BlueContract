import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './http';
import type { components } from './schema';

type S = components['schemas'];

export type PaymentRecordStatus = S['PaymentRecordStatus'];
export type AcceptanceListItemDto = S['AcceptanceListItemDto'];
export type AcceptanceDto = S['AcceptanceDto'];
export type AcceptanceReceiptDto = S['AcceptanceReceiptDto'];
export type AcceptanceItemDto = S['AcceptanceItemDto'];
export type SaveAcceptanceRequest = S['SaveAcceptanceRequest'];
export type GenerateAcceptancesResultDto = S['GenerateAcceptancesResultDto'];
export type ValuationListItemDto = S['ValuationListItemDto'];
export type ValuationDto = S['ValuationDto'];
export type ValuationItemDto = S['ValuationItemDto'];
export type PaymentOrderDto = S['PaymentOrderDto'];
export type SaveValuationRequest = S['SaveValuationRequest'];
export type LiquidationListItemDto = S['LiquidationListItemDto'];
export type LiquidationDto = S['LiquidationDto'];
export type LiquidationPreviewDto = S['LiquidationPreviewDto'];
export type LiquidationValuationDto = S['LiquidationValuationDto'];
export type LiquidationSettlementDto = S['LiquidationSettlementDto'];
export type FileLinkDto = S['FileLinkDto'];

export const ACCEPTANCES = '/api/acceptances';
export const VALUATIONS = '/api/valuations';
export const LIQUIDATIONS = '/api/liquidations';

/** Payment actions move workflows, documents and contract tracking: refresh everything. */
export function usePaymentAction<TArg = void, TResult = unknown>(fn: (arg: TArg) => Promise<TResult>) {
  const qc = useQueryClient();
  return useMutation({ mutationFn: fn, onSuccess: () => qc.invalidateQueries() });
}

const get =
  <T>(url: string, params?: object) =>
  async () =>
    (await apiClient.get<T>(url, { params })).data;

export const useAcceptance = (id?: string) =>
  useQuery({
    queryKey: [ACCEPTANCES, id],
    queryFn: get<AcceptanceDto>(`${ACCEPTANCES}/${id}`),
    enabled: !!id,
  });

export const useAcceptanceCandidates = (
  contractId?: string,
  from?: string,
  to?: string,
  acceptanceId?: string,
) =>
  useQuery({
    queryKey: [ACCEPTANCES, 'candidates', contractId, from, to, acceptanceId],
    queryFn: get<AcceptanceReceiptDto[]>(`${ACCEPTANCES}/candidates`, { contractId, from, to, acceptanceId }),
    enabled: !!contractId,
  });

export const useValuation = (id?: string) =>
  useQuery({ queryKey: [VALUATIONS, id], queryFn: get<ValuationDto>(`${VALUATIONS}/${id}`), enabled: !!id });

export const useLiquidation = (id?: string) =>
  useQuery({
    queryKey: [LIQUIDATIONS, id],
    queryFn: get<LiquidationDto>(`${LIQUIDATIONS}/${id}`),
    enabled: !!id,
  });

export const useLiquidationPreview = (contractId?: string) =>
  useQuery({
    queryKey: [LIQUIDATIONS, 'preview', contractId],
    queryFn: get<LiquidationPreviewDto>(`${LIQUIDATIONS}/preview`, { contractId }),
    enabled: !!contractId,
  });

const form = (fields: Record<string, string | number | null | undefined>, file?: File | null) => {
  const data = new FormData();
  Object.entries(fields).forEach(
    ([k, v]) => v !== null && v !== undefined && v !== '' && data.append(k, String(v)),
  );
  if (file) data.append('file', file);
  return data;
};

const LONG = { timeout: 180_000 };

export const acceptanceApi = {
  create: async (body: SaveAcceptanceRequest) => (await apiClient.post<string>(ACCEPTANCES, body, LONG)).data,
  update: async (id: string, body: SaveAcceptanceRequest) => {
    await apiClient.put(`${ACCEPTANCES}/${id}`, body, LONG);
  },
  submit: async (id: string) => {
    await apiClient.post(`${ACCEPTANCES}/${id}/submit`, null, LONG);
  },
  cancel: async (id: string) => {
    await apiClient.post(`${ACCEPTANCES}/${id}/cancel`);
  },
  generate: async () =>
    (
      await apiClient.post<GenerateAcceptancesResultDto>(`${ACCEPTANCES}/generate`, null, {
        timeout: 300_000,
      })
    ).data,
};

export const valuationApi = {
  create: async (body: SaveValuationRequest) =>
    (await apiClient.post<ValuationDto>(VALUATIONS, body, LONG)).data,
  update: async (id: string, body: SaveValuationRequest) => {
    await apiClient.put(`${VALUATIONS}/${id}`, body, LONG);
  },
  submit: async (id: string) => {
    await apiClient.post(`${VALUATIONS}/${id}/submit`, null, LONG);
  },
  cancel: async (id: string) => {
    await apiClient.post(`${VALUATIONS}/${id}/cancel`);
  },
  addPaymentOrder: async (
    id: string,
    v: { number: string; date: string; amount: number },
    file?: File | null,
  ) => (await apiClient.post<ValuationDto>(`${VALUATIONS}/${id}/payment-orders`, form(v, file), LONG)).data,
  removePaymentOrder: async (id: string, orderId: string) =>
    (await apiClient.delete<ValuationDto>(`${VALUATIONS}/${id}/payment-orders/${orderId}`)).data,
  paymentOrderLink: async (id: string, orderId: string) =>
    (await apiClient.get<FileLinkDto>(`${VALUATIONS}/${id}/payment-orders/${orderId}/file`)).data,
};

export const liquidationApi = {
  create: async (body: { contractId: string; notes: string | null }) =>
    (await apiClient.post<string>(LIQUIDATIONS, body, LONG)).data,
  update: async (id: string, body: { contractId: string; notes: string | null }) => {
    await apiClient.put(`${LIQUIDATIONS}/${id}`, body, LONG);
  },
  submit: async (id: string) => {
    await apiClient.post(`${LIQUIDATIONS}/${id}/submit`, null, LONG);
  },
  cancel: async (id: string) => {
    await apiClient.post(`${LIQUIDATIONS}/${id}/cancel`);
  },
  addSettlement: async (
    id: string,
    v: {
      valuationId?: string | null;
      description: string;
      amount: number;
      voucherNumber?: string | null;
      voucherDate?: string | null;
    },
    file?: File | null,
  ) => (await apiClient.post<LiquidationDto>(`${LIQUIDATIONS}/${id}/settlements`, form(v, file), LONG)).data,
  removeSettlement: async (id: string, settlementId: string) =>
    (await apiClient.delete<LiquidationDto>(`${LIQUIDATIONS}/${id}/settlements/${settlementId}`, LONG)).data,
};
