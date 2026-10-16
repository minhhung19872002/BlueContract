import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './http';
import type { components } from './schema';

type S = components['schemas'];

export type ProcurementDomain = S['ProcurementDomain'];
export type ProposalStatus = S['ProposalStatus'];
export type ProposalListItemDto = S['ProposalListItemDto'];
export type ProposalDto = S['ProposalDto'];
export type ProposalLineDto = S['ProposalLineDto'];
export type ProposalCandidateDto = S['ProposalCandidateDto'];
export type ProposalCandidatesDto = S['ProposalCandidatesDto'];
export type SaveProposalRequest = S['SaveProposalRequest'];
export type SummaryListItemDto = S['SummaryListItemDto'];
export type SummaryDto = S['SummaryDto'];
export type SummaryLineDto = S['SummaryLineDto'];
export type SaveSummaryRequest = S['SaveSummaryRequest'];
export type CallOrderListItemDto = S['CallOrderListItemDto'];
export type CallOrderDto = S['CallOrderDto'];
export type CallOrderLineDto = S['CallOrderLineDto'];
export type CreateCallsRequest = S['CreateCallsRequest'];
export type ConfirmCallRequest = S['ConfirmCallRequest'];

export const PROPOSALS = '/api/proposals';
export const SUMMARIES = '/api/proposal-summaries';
export const CALL_ORDERS = '/api/call-orders';

export const DOMAIN_OPTIONS: { value: ProcurementDomain; label: string }[] = [
  { value: 'Drug', label: 'Thuốc' },
  { value: 'MedicalSupply', label: 'Vật tư y tế' },
  { value: 'Chemical', label: 'Hóa chất, sinh phẩm' },
  { value: 'Equipment', label: 'Tài sản, trang thiết bị' },
  { value: 'Administration', label: 'Hành chính, dịch vụ' },
];

export const domainLabel = (d?: ProcurementDomain | null) =>
  DOMAIN_OPTIONS.find((o) => o.value === d)?.label ?? '';

/** Mutations move workflows, documents and contract tracking: refresh everything related. */
export function useProposalAction<TArg = void, TResult = unknown>(fn: (arg: TArg) => Promise<TResult>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: () =>
      Promise.all(
        [
          [PROPOSALS],
          [SUMMARIES],
          [CALL_ORDERS],
          ['/api/contracts'],
          ['/api/workflows'],
          ['/api/signing'],
          ['/api/documents'],
        ].map((queryKey) => qc.invalidateQueries({ queryKey })),
      ),
  });
}

export function useProposal(id: string | undefined) {
  return useQuery({
    queryKey: [PROPOSALS, id],
    queryFn: async () => (await apiClient.get<ProposalDto>(`${PROPOSALS}/${id}`)).data,
    enabled: !!id,
  });
}

export function useProposalCandidates(params: {
  domain?: ProcurementDomain;
  warehouseId?: string;
  from?: string;
  to?: string;
  departmentId?: string;
}) {
  const ready = !!params.domain && !!params.warehouseId && !!params.from && !!params.to;
  return useQuery({
    queryKey: [PROPOSALS, 'candidates', params],
    queryFn: async () =>
      (await apiClient.get<ProposalCandidatesDto>(`${PROPOSALS}/candidates`, { params, timeout: 60_000 }))
        .data,
    enabled: ready,
  });
}

export function useSummary(id: string | undefined) {
  return useQuery({
    queryKey: [SUMMARIES, id],
    queryFn: async () => (await apiClient.get<SummaryDto>(`${SUMMARIES}/${id}`)).data,
    enabled: !!id,
  });
}

export function useSummaryCandidates(domain: ProcurementDomain | undefined) {
  return useQuery({
    queryKey: [SUMMARIES, 'candidates', domain],
    queryFn: async () =>
      (await apiClient.get<ProposalListItemDto[]>(`${SUMMARIES}/candidates`, { params: { domain } })).data,
    enabled: !!domain,
  });
}

export function useCallOrder(id: string | undefined) {
  return useQuery({
    queryKey: [CALL_ORDERS, id],
    queryFn: async () => (await apiClient.get<CallOrderDto>(`${CALL_ORDERS}/${id}`)).data,
    enabled: !!id,
  });
}

export const proposalApi = {
  create: async (body: SaveProposalRequest) =>
    (await apiClient.post<string>(PROPOSALS, body, { timeout: 120_000 })).data,
  update: async (id: string, body: SaveProposalRequest) => {
    await apiClient.put(`${PROPOSALS}/${id}`, body, { timeout: 120_000 });
  },
  submit: async (id: string) => {
    await apiClient.post(`${PROPOSALS}/${id}/submit`, null, { timeout: 180_000 });
  },
  cancel: async (id: string) => {
    await apiClient.post(`${PROPOSALS}/${id}/cancel`);
  },
};

export const summaryApi = {
  create: async (body: SaveSummaryRequest) =>
    (await apiClient.post<string>(SUMMARIES, body, { timeout: 120_000 })).data,
  update: async (id: string, body: SaveSummaryRequest) => {
    await apiClient.put(`${SUMMARIES}/${id}`, body, { timeout: 120_000 });
  },
  submit: async (id: string) => {
    await apiClient.post(`${SUMMARIES}/${id}/submit`, null, { timeout: 180_000 });
  },
  setApproved: async (id: string, lines: { lineId: string; approvedQuantity: number }[]) => {
    await apiClient.put(`${SUMMARIES}/${id}/approved-quantities`, lines, { timeout: 120_000 });
  },
  cancel: async (id: string) => {
    await apiClient.post(`${SUMMARIES}/${id}/cancel`);
  },
};

export const callOrderApi = {
  create: async (body: CreateCallsRequest) =>
    (await apiClient.post<string[]>(CALL_ORDERS, body, { timeout: 180_000 })).data,
  confirm: async (id: string, body: ConfirmCallRequest) => {
    await apiClient.post(`${CALL_ORDERS}/${id}/confirm`, body);
  },
  cancel: async (id: string) => {
    await apiClient.post(`${CALL_ORDERS}/${id}/cancel`);
  },
};
