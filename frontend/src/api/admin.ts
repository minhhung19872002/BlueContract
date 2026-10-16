import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './http';
import type { components } from './schema';

type S = components['schemas'];

export type Paged<T> = { items: T[]; total: number; page: number; pageSize: number };
export type ListParams = Record<string, string | number | boolean | undefined | null>;

export type OrganizationDto = S['OrganizationDto'];
export type FacilityDto = S['FacilityDto'];
export type DepartmentDto = S['DepartmentDto'];
export type WarehouseDto = S['WarehouseDto'];
export type SupplierDto = S['SupplierDto'];
export type UserListItemDto = S['UserListItemDto'];
export type UserDetailDto = S['UserDetailDto'];
export type RoleDto = S['RoleDto'];
export type PermissionMatrixDto = S['PermissionMatrixDto'];
export type DelegationDto = S['DelegationDto'];
export type SystemSettingDto = S['SystemSettingDto'];
export type AuditLogDto = S['AuditLogDto'];
export type HistoryEntryDto = S['HistoryEntryDto'];
export type LookupItem = S['LookupItem'];
export type SupplierStaffDto = S['SupplierStaffDto'];

function clean(params: ListParams) {
  return Object.fromEntries(
    Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== ''),
  );
}

/** Server-side paged list bound to URL params by the caller. */
export function usePagedList<T>(resource: string, params: ListParams) {
  return useQuery({
    queryKey: [resource, 'list', clean(params)],
    queryFn: async () => (await apiClient.get<Paged<T>>(resource, { params: clean(params) })).data,
    placeholderData: keepPreviousData,
  });
}

export function useGet<T>(url: string | null, key?: unknown[]) {
  return useQuery({
    queryKey: key ?? [url],
    queryFn: async () => (await apiClient.get<T>(url!)).data,
    enabled: !!url,
  });
}

/** POST (no id) or PUT (with id) to a resource; invalidates the resource's queries on success. */
export function useSave<TBody, TResult = unknown>(resource: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: { id?: string; body: TBody }) =>
      (id
        ? await apiClient.put<TResult>(`${resource}/${id}`, body)
        : await apiClient.post<TResult>(resource, body)
      ).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: [resource] }),
  });
}

export function useAction<TBody = void, TResult = unknown>(
  resource: string,
  build: (arg: TBody) => { url: string; method?: 'post' | 'put' | 'delete'; body?: unknown },
) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (arg: TBody) => {
      const { url, method = 'post', body } = build(arg);
      return (await apiClient.request<TResult>({ url, method, data: body })).data;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: [resource] }),
  });
}

// ---- Lookups (select options) ----

export function useLookup(
  kind: 'facilities' | 'departments' | 'warehouses' | 'suppliers' | 'roles' | 'internal-users',
  params?: ListParams,
) {
  return useQuery({
    queryKey: ['lookups', kind, clean(params ?? {})],
    queryFn: async () =>
      (await apiClient.get<LookupItem[]>(`/api/lookups/${kind}`, { params: clean(params ?? {}) })).data,
    staleTime: 5 * 60_000,
  });
}

export const resources = {
  organization: '/api/admin/organization',
  facilities: '/api/admin/facilities',
  departments: '/api/admin/departments',
  warehouses: '/api/admin/warehouses',
  suppliers: '/api/admin/suppliers',
  users: '/api/admin/users',
  roles: '/api/admin/roles',
  delegations: '/api/admin/delegations',
  settings: '/api/admin/settings',
  auditLogs: '/api/admin/audit-logs',
  supplierCompany: '/api/supplier/company',
} as const;
