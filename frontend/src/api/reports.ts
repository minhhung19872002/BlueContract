import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './http';
import type { components } from './schema';

type S = components['schemas'];

export type InternalDashboardDto = S['InternalDashboardDto'];
export type SupplierDashboardDto = S['SupplierDashboardDto'];
export type MonthAmountDto = S['MonthAmountDto'];
export type ArchiveSearchItemDto = S['ArchiveSearchItemDto'];
export type ArchiveTreeDto = S['ArchiveTreeDto'];
export type ArchiveNodeDto = S['ArchiveNodeDto'];
export type ArchiveFileDto = S['ArchiveFileDto'];
export type ContractExecutionRowDto = S['ContractExecutionRowDto'];
export type SupplierDebtRowDto = S['SupplierDebtRowDto'];
export type PaymentReportRowDto = S['PaymentReportRowDto'];
export type SigningReportDto = S['SigningReportDto'];
export type PaymentPeriod = S['PaymentPeriod'];
export type PaymentDimension = S['PaymentDimension'];
export type FileLinkDto = S['FileLinkDto'];

export const ARCHIVE = '/api/archive';
export const REPORTS = '/api/reports';

const get =
  <T>(url: string, params?: object) =>
  async () =>
    (await apiClient.get<T>(url, { params })).data;

export const useInternalDashboard = (enabled = true) =>
  useQuery({
    queryKey: ['/api/dashboard'],
    queryFn: get<InternalDashboardDto>('/api/dashboard'),
    enabled,
    refetchInterval: 60_000,
  });

export const useSupplierDashboard = (enabled = true) =>
  useQuery({
    queryKey: ['/api/dashboard', 'supplier'],
    queryFn: get<SupplierDashboardDto>('/api/dashboard/supplier'),
    refetchInterval: 60_000,
    enabled,
  });

export const useArchiveTree = (contractId?: string) =>
  useQuery({
    queryKey: [ARCHIVE, contractId],
    queryFn: get<ArchiveTreeDto>(`${ARCHIVE}/${contractId}`),
    enabled: !!contractId,
  });

export const archiveApi = {
  fileLink: async (contractId: string, fileId: string) =>
    (await apiClient.get<FileLinkDto>(`${ARCHIVE}/${contractId}/files/${fileId}`)).data,
};

export function useArchiveComplete(contractId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (complete: boolean) =>
      (await apiClient.put<ArchiveTreeDto>(`${ARCHIVE}/${contractId}/complete`, { complete })).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: [ARCHIVE] }),
  });
}

export const useContractExecution = (params: object) =>
  useQuery({
    queryKey: [REPORTS, 'contract-execution', params],
    queryFn: get<ContractExecutionRowDto[]>(`${REPORTS}/contract-execution`, params),
  });

export const useSupplierDebt = () =>
  useQuery({
    queryKey: [REPORTS, 'supplier-debt'],
    queryFn: get<SupplierDebtRowDto[]>(`${REPORTS}/supplier-debt`),
  });

export const usePaymentReport = (params: {
  year?: number;
  period: PaymentPeriod;
  dimension: PaymentDimension;
}) =>
  useQuery({
    queryKey: [REPORTS, 'payments', params],
    queryFn: get<PaymentReportRowDto[]>(`${REPORTS}/payments`, params),
  });

export const useSigningReport = () =>
  useQuery({ queryKey: [REPORTS, 'signing'], queryFn: get<SigningReportDto>(`${REPORTS}/signing`) });

type IntegrityRunDto = components['schemas']['IntegrityRunDto'];
export const INTEGRITY = '/api/admin/integrity';

export const useIntegrityRun = (id: string | null) =>
  useQuery({
    queryKey: [INTEGRITY, id],
    queryFn: async () => (await apiClient.get<IntegrityRunDto>(`${INTEGRITY}/${id}`)).data,
    enabled: !!id,
  });

/** Full re-hash of every stored file: can take minutes on a large store. */
export function useRunIntegrityCheck() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () =>
      (await apiClient.post<IntegrityRunDto>(`${INTEGRITY}/run`, null, { timeout: 600_000 })).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: [INTEGRITY] }),
  });
}
