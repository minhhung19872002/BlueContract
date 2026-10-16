import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient, downloadFile } from './http';
import type { components } from './schema';

type S = components['schemas'];

export type TemplateKind = S['TemplateKind'];
export type TemplateListItemDto = S['TemplateListItemDto'];
export type TemplateDetailDto = S['TemplateDetailDto'];
export type TemplateFieldsDto = S['TemplateFieldsDto'];
export type TemplateValidationResult = S['TemplateValidationResult'];
export type DocumentDto = S['DocumentDto'];
export type DocumentVersionDto = S['DocumentVersionDto'];
export type EditorMode = S['EditorMode'];
export type EditorSessionDto = S['EditorSessionDto'];
export type DocumentCompareDto = S['DocumentCompareDto'];
export type DiffLine = S['DiffLine'];

const TEMPLATES = '/api/templates';
const DOCUMENTS = '/api/documents';

function form(file: File, fields: Record<string, string | undefined>) {
  const data = new FormData();
  data.append('file', file);
  Object.entries(fields).forEach(([k, v]) => v !== undefined && data.append(k, v));
  return data;
}

// ---------------- Templates ----------------

export function useTemplates(kind?: TemplateKind) {
  return useQuery({
    queryKey: [TEMPLATES, 'list', kind ?? 'all'],
    queryFn: async () => (await apiClient.get<TemplateListItemDto[]>(TEMPLATES, { params: { kind } })).data,
  });
}

export function useTemplate(id: string | null) {
  return useQuery({
    queryKey: [TEMPLATES, 'detail', id],
    queryFn: async () => (await apiClient.get<TemplateDetailDto>(`${TEMPLATES}/${id}`)).data,
    enabled: !!id,
  });
}

export function useTemplateFields(kind: TemplateKind | undefined) {
  return useQuery({
    queryKey: [TEMPLATES, 'fields', kind],
    queryFn: async () =>
      (await apiClient.get<TemplateFieldsDto>(`${TEMPLATES}/fields`, { params: { kind } })).data,
    enabled: !!kind,
    staleTime: Infinity,
  });
}

export function useValidateTemplate() {
  return useMutation({
    mutationFn: async ({ kind, file }: { kind: TemplateKind; file: File }) =>
      (await apiClient.post<TemplateValidationResult>(`${TEMPLATES}/validate`, form(file, { kind }))).data,
  });
}

export function useCreateTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (v: {
      code: string;
      name: string;
      kind: TemplateKind;
      description?: string;
      file: File;
    }) =>
      (
        await apiClient.post<string>(
          TEMPLATES,
          form(v.file, { code: v.code, name: v.name, kind: v.kind, description: v.description }),
        )
      ).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: [TEMPLATES] }),
  });
}

export function useUploadTemplateVersion(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (v: { file: File; note?: string }) =>
      (await apiClient.post<number>(`${TEMPLATES}/${id}/versions`, form(v.file, { note: v.note }))).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: [TEMPLATES] }),
  });
}

export function useActivateTemplateVersion(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (version: number) => {
      await apiClient.post(`${TEMPLATES}/${id}/versions/${version}/activate`);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: [TEMPLATES] }),
  });
}

export function useUpdateTemplate(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: S['UpdateTemplateRequest']) => {
      await apiClient.put(`${TEMPLATES}/${id}`, body);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: [TEMPLATES] }),
  });
}

/** Renders the template with sample data and returns a PDF blob for the viewer. */
export async function fetchTemplatePreview(id: string, version?: number): Promise<Blob> {
  return (
    await apiClient.get<Blob>(`${TEMPLATES}/${id}/preview`, {
      params: { version, format: 'pdf' },
      responseType: 'blob',
      timeout: 120_000,
    })
  ).data;
}

export function downloadTemplate(id: string, version: number, fileName: string) {
  return downloadFile(`${TEMPLATES}/${id}/download`, fileName, { params: { version } });
}

export function downloadTemplatePreviewDocx(id: string, fileName: string) {
  return downloadFile(`${TEMPLATES}/${id}/preview`, fileName, { params: { format: 'docx' } });
}

// ---------------- Documents ----------------

export function useCreateSampleDocument() {
  return useMutation({
    mutationFn: async (body: S['CreateSampleDocumentRequest']) =>
      (await apiClient.post<string>(`${DOCUMENTS}/samples`, body)).data,
  });
}

export function useDocument(id: string | undefined, poll = false) {
  return useQuery({
    queryKey: [DOCUMENTS, id],
    queryFn: async () => (await apiClient.get<DocumentDto>(`${DOCUMENTS}/${id}`)).data,
    enabled: !!id,
    refetchInterval: poll ? 3000 : false,
  });
}

export function useRenderPdf(documentId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (versionId: string) =>
      (
        await apiClient.post<DocumentVersionDto>(
          `${DOCUMENTS}/${documentId}/versions/${versionId}/pdf`,
          null,
          {
            timeout: 180_000,
          },
        )
      ).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: [DOCUMENTS, documentId] }),
  });
}

export function useUploadDocumentVersion(documentId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (v: { file: File; reason?: string }) =>
      (
        await apiClient.post<string>(
          `${DOCUMENTS}/${documentId}/versions`,
          form(v.file, { reason: v.reason }),
        )
      ).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: [DOCUMENTS, documentId] }),
  });
}

export function useStartEditSession(documentId: string) {
  return useMutation({
    mutationFn: async (mode: EditorMode) =>
      (await apiClient.post<EditorSessionDto>(`${DOCUMENTS}/${documentId}/edit-sessions`, { mode })).data,
  });
}

/** Fetches a version file through the API (permission-checked, audited). */
export async function fetchDocumentFile(
  documentId: string,
  versionId: string,
  format: 'pdf' | 'docx',
): Promise<Blob> {
  return (
    await apiClient.get<Blob>(`${DOCUMENTS}/${documentId}/versions/${versionId}/file`, {
      params: { format },
      responseType: 'blob',
    })
  ).data;
}

export function downloadDocumentFile(
  documentId: string,
  versionId: string,
  format: 'pdf' | 'docx',
  fileName: string,
) {
  return downloadFile(`${DOCUMENTS}/${documentId}/versions/${versionId}/file`, fileName, {
    params: { format },
  });
}

/** Paragraph diff of two versions (DOCX, tracked changes accepted). */
export function useDocumentCompare(documentId: string, from?: string, to?: string) {
  return useQuery({
    queryKey: [DOCUMENTS, documentId, 'compare', from, to],
    queryFn: async () =>
      (
        await apiClient.get<DocumentCompareDto>(`${DOCUMENTS}/${documentId}/compare`, {
          params: { from, to },
        })
      ).data,
    enabled: !!from && !!to && from !== to,
  });
}
