import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './http';
import type { components } from './schema';

type S = components['schemas'];

export type ReceiptListItemDto = S['ReceiptListItemDto'];
export type ReceiptDto = S['ReceiptDto'];
export type ReceiptLineDto = S['ReceiptLineDto'];
export type ReceiptIssueDto = S['ReceiptIssueDto'];
export type ReceiptType = S['ReceiptType'];
export type ReceiptStatus = S['ReceiptStatus'];
export type SaveReceiptRequest = S['SaveReceiptRequest'];
export type InvoiceRequest = S['InvoiceRequest'];
export type ParsedInvoiceDto = S['ParsedInvoiceDto'];
export type FileLinkDto = S['FileLinkDto'];
export type SimulateReceiptRequest = S['SimulateReceiptRequest'];
export type SimulateReceiptResultDto = S['SimulateReceiptResultDto'];
export type IntegrationLogListItemDto = S['IntegrationLogListItemDto'];
export type IntegrationLogDto = S['IntegrationLogDto'];
export type HisIntegrationConfigDto = S['HisIntegrationConfigDto'];
export type PullResultDto = S['PullResultDto'];
export type HisIngestResultDto = S['HisIngestResultDto'];

export const RECEIPTS = '/api/warehouse-receipts';
export const INTEGRATION = '/api/admin/integration';

/** Receipt actions move workflows, documents and contract tracking: refresh everything (cheap with TanStack Query). */
export function useReceiptAction<TArg = void, TResult = unknown>(fn: (arg: TArg) => Promise<TResult>) {
  const qc = useQueryClient();
  return useMutation({ mutationFn: fn, onSuccess: () => qc.invalidateQueries() });
}

export function useReceipt(id: string | undefined) {
  return useQuery({
    queryKey: [RECEIPTS, id],
    queryFn: async () => (await apiClient.get<ReceiptDto>(`${RECEIPTS}/${id}`)).data,
    enabled: !!id,
  });
}

export function useDevHisStatus() {
  return useQuery({
    queryKey: ['/api/dev/his/status'],
    queryFn: async () => (await apiClient.get<{ enabled: boolean }>('/api/dev/his/status')).data,
    staleTime: 5 * 60_000,
  });
}

export function useIntegrationConfig() {
  return useQuery({
    queryKey: [INTEGRATION, 'config'],
    queryFn: async () => (await apiClient.get<HisIntegrationConfigDto>(`${INTEGRATION}/config`)).data,
  });
}

export function useIntegrationLog(id: string | null) {
  return useQuery({
    queryKey: [INTEGRATION, 'logs', id],
    queryFn: async () => (await apiClient.get<IntegrationLogDto>(`${INTEGRATION}/logs/${id}`)).data,
    enabled: !!id,
  });
}

const upload = (file: File) => {
  const form = new FormData();
  form.append('file', file);
  return form;
};

export const receiptApi = {
  create: async (body: SaveReceiptRequest) =>
    (await apiClient.post<string>(RECEIPTS, body, { timeout: 120_000 })).data,
  update: async (id: string, body: SaveReceiptRequest) => {
    await apiClient.put(`${RECEIPTS}/${id}`, body, { timeout: 120_000 });
  },
  submit: async (id: string) => {
    await apiClient.post(`${RECEIPTS}/${id}/submit`, null, { timeout: 180_000 });
  },
  revalidate: async (id: string) => (await apiClient.post<ReceiptDto>(`${RECEIPTS}/${id}/revalidate`)).data,
  cancel: async (id: string, reason?: string) => {
    await apiClient.post(`${RECEIPTS}/${id}/cancel`, { reason: reason ?? null });
  },
  setInvoice: async (id: string, body: InvoiceRequest) =>
    (await apiClient.put<ReceiptDto>(`${RECEIPTS}/${id}/invoice`, body, { timeout: 120_000 })).data,
  uploadInvoiceFile: async (id: string, file: File) =>
    (await apiClient.post<ReceiptDto>(`${RECEIPTS}/${id}/invoice-file`, upload(file), { timeout: 120_000 }))
      .data,
  parseInvoice: async (file: File) =>
    (await apiClient.post<ParsedInvoiceDto>(`${RECEIPTS}/invoice/parse`, upload(file))).data,
  invoiceLink: async (id: string, format: 'xml' | 'pdf') =>
    (await apiClient.get<FileLinkDto>(`${RECEIPTS}/${id}/invoice-file/${format}`)).data,
  simulate: async (body: SimulateReceiptRequest) =>
    (
      await apiClient.post<SimulateReceiptResultDto>('/api/dev/his/simulate-receipt', body, {
        timeout: 180_000,
      })
    ).data,
};

export const integrationApi = {
  retry: async (id: string) =>
    (await apiClient.post<HisIngestResultDto>(`${INTEGRATION}/logs/${id}/retry`, null, { timeout: 180_000 }))
      .data,
  pull: async () =>
    (await apiClient.post<PullResultDto>(`${INTEGRATION}/his/pull`, null, { timeout: 300_000 })).data,
};
