import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient, downloadFile } from './http';
import type { components } from './schema';

type S = components['schemas'];

export type ItemCategoryDto = S['ItemCategoryDto'];
export type ItemDto = S['ItemDto'];
export type ItemImportKind = S['ItemImportKind'];
export type ItemSource = S['ItemSource'];
export type SaveItemRequest = S['SaveItemRequest'];
export type BidDecisionListItemDto = S['BidDecisionListItemDto'];
export type BidDecisionDto = S['BidDecisionDto'];
export type BidItemDto = S['BidItemDto'];
export type BidSupplierDto = S['BidSupplierDto'];
export type SaveBidDecisionRequest = S['SaveBidDecisionRequest'];
export type BidItemRequest = S['BidItemRequest'];
export type HisSyncResultDto = S['HisSyncResultDto'];
export type ImportPreviewDto = S['ImportPreviewDto'];
export type ImportRowDto = S['ImportRowDto'];
export type ImportResultDto = S['ImportResultDto'];
export type ContractListItemDto = S['ContractListItemDto'];
export type ContractDto = S['ContractDto'];
export type ContractItemDto = S['ContractItemDto'];
export type ContractStatus = S['ContractStatus'];
export type ProcurementType = S['ProcurementType'];
export type ContractKind = S['ContractKind'];
export type SaveContractRequest = S['SaveContractRequest'];
export type ContractLineRequest = S['ContractLineRequest'];
export type ContractAppendixDto = S['ContractAppendixDto'];
export type SaveAppendixRequest = S['SaveAppendixRequest'];
export type AppendixType = S['AppendixType'];
export type RevisionListDto = S['RevisionListDto'];
export type DocxRevision = S['DocxRevision'];

export const ITEMS = '/api/items';
export const BIDS = '/api/bid-decisions';
export const CONTRACTS = '/api/contracts';

function upload(file: File) {
  const data = new FormData();
  data.append('file', file);
  return data;
}

// ---------------- Catalog ----------------

export function useItemCategories() {
  return useQuery({
    queryKey: [ITEMS, 'categories'],
    queryFn: async () => (await apiClient.get<ItemCategoryDto[]>(`${ITEMS}/categories`)).data,
    staleTime: Infinity,
  });
}

export function downloadImportTemplate(kind: ItemImportKind) {
  return downloadFile(
    `${ITEMS}/import-template`,
    `bieu-mau-import-${kind === 'Medical' ? 'thuoc-vtyt' : 'hang-hoa-dich-vu'}.xlsx`,
    {
      params: { kind },
    },
  );
}

// ---------------- Bid decisions ----------------

export function useBidDecision(id: string | undefined) {
  return useQuery({
    queryKey: [BIDS, id],
    queryFn: async () => (await apiClient.get<BidDecisionDto>(`${BIDS}/${id}`)).data,
    enabled: !!id,
  });
}

export function useBidItems(id: string | undefined, supplierId?: string) {
  return useQuery({
    queryKey: [BIDS, id, 'items', supplierId ?? 'all'],
    queryFn: async () =>
      (await apiClient.get<BidItemDto[]>(`${BIDS}/${id}/items`, { params: { supplierId } })).data,
    enabled: !!id,
  });
}

/** Any mutation on a bid decision refreshes its queries (and item lists). */
export function useBidAction<TArg = void, TResult = unknown>(fn: (arg: TArg) => Promise<TResult>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: () =>
      Promise.all([qc.invalidateQueries({ queryKey: [BIDS] }), qc.invalidateQueries({ queryKey: [ITEMS] })]),
  });
}

export const bidApi = {
  create: async (body: SaveBidDecisionRequest) => (await apiClient.post<string>(BIDS, body)).data,
  update: async (id: string, body: SaveBidDecisionRequest) => {
    await apiClient.put(`${BIDS}/${id}`, body);
  },
  attach: async (id: string, file: File) => {
    await apiClient.post(`${BIDS}/${id}/attachment`, upload(file));
  },
  attachmentBlob: async (id: string) =>
    (await apiClient.get<Blob>(`${BIDS}/${id}/attachment`, { responseType: 'blob' })).data,
  upsertSupplier: async (id: string, supplierId: string, winningValue: number) => {
    await apiClient.put(`${BIDS}/${id}/suppliers`, { supplierId, winningValue });
  },
  removeSupplier: async (id: string, supplierId: string) => {
    await apiClient.delete(`${BIDS}/${id}/suppliers/${supplierId}`);
  },
  addItem: async (id: string, body: BidItemRequest) =>
    (await apiClient.post<string>(`${BIDS}/${id}/items`, body)).data,
  removeItem: async (id: string, itemId: string) => {
    await apiClient.delete(`${BIDS}/${id}/items/${itemId}`);
  },
  syncHis: async (id: string, supplierId?: string) =>
    (
      await apiClient.post<HisSyncResultDto>(`${BIDS}/${id}/his-sync`, null, {
        params: { supplierId },
        timeout: 120_000,
      })
    ).data,
  previewImport: async (id: string, file: File) =>
    (
      await apiClient.post<ImportPreviewDto>(`${BIDS}/${id}/import/preview`, upload(file), {
        timeout: 120_000,
      })
    ).data,
  commitImport: async (id: string, file: File) =>
    (await apiClient.post<ImportResultDto>(`${BIDS}/${id}/import`, upload(file), { timeout: 120_000 })).data,
  activate: async (id: string) => {
    await apiClient.post(`${BIDS}/${id}/activate`);
  },
  close: async (id: string) => {
    await apiClient.post(`${BIDS}/${id}/close`);
  },
};

// ---------------- Contracts ----------------

export function useContract(id: string | undefined) {
  return useQuery({
    queryKey: [CONTRACTS, id],
    queryFn: async () => (await apiClient.get<ContractDto>(`${CONTRACTS}/${id}`)).data,
    enabled: !!id,
  });
}

export function useContractRevisions(id: string | undefined, enabled: boolean) {
  return useQuery({
    queryKey: [CONTRACTS, id, 'revisions'],
    queryFn: async () => (await apiClient.get<RevisionListDto>(`${CONTRACTS}/${id}/revisions`)).data,
    enabled: !!id && enabled,
  });
}

/** Contract mutations also move workflows and documents: refresh all of them. */
export function useContractAction<TArg = void, TResult = unknown>(fn: (arg: TArg) => Promise<TResult>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: () =>
      Promise.all(
        [[CONTRACTS], [BIDS], ['/api/documents'], ['/api/workflows'], ['/api/signing']].map((queryKey) =>
          qc.invalidateQueries({ queryKey }),
        ),
      ),
  });
}

export const contractApi = {
  create: async (body: SaveContractRequest) =>
    (await apiClient.post<string>(CONTRACTS, body, { timeout: 120_000 })).data,
  update: async (id: string, body: SaveContractRequest) => {
    await apiClient.put(`${CONTRACTS}/${id}`, body, { timeout: 120_000 });
  },
  preview: async (body: SaveContractRequest) =>
    (await apiClient.post<Blob>(`${CONTRACTS}/preview`, body, { responseType: 'blob', timeout: 180_000 }))
      .data,
  submit: async (id: string) => {
    await apiClient.post(`${CONTRACTS}/${id}/submit`, null, { timeout: 180_000 });
  },
  cancel: async (id: string, reason: string) => {
    await apiClient.post(`${CONTRACTS}/${id}/cancel`, { reason });
  },
  supplierResponse: async (id: string, agree: boolean, comment?: string) => {
    await apiClient.post(
      `${CONTRACTS}/${id}/supplier-response`,
      { agree, comment: comment ?? null },
      { timeout: 180_000 },
    );
  },
  supplierRevision: async (id: string, file: File) =>
    (await apiClient.post<string>(`${CONTRACTS}/${id}/supplier-revision`, upload(file))).data,
  resolveRevisions: async (id: string, versionId: string, decisions: { id: string; accept: boolean }[]) =>
    (await apiClient.post<string>(`${CONTRACTS}/${id}/revisions`, { versionId, decisions })).data,
  createAppendix: async (id: string, body: SaveAppendixRequest) =>
    (await apiClient.post<string>(`${CONTRACTS}/${id}/appendices`, body, { timeout: 120_000 })).data,
  submitAppendix: async (appendixId: string) => {
    await apiClient.post(`${CONTRACTS}/appendices/${appendixId}/submit`, null, { timeout: 180_000 });
  },
  cancelAppendix: async (appendixId: string) => {
    await apiClient.post(`${CONTRACTS}/appendices/${appendixId}/cancel`);
  },
};
