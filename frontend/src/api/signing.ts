import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './http';
import type { components } from './schema';

type S = components['schemas'];

export type WorkflowDto = S['WorkflowDto'];
export type WorkflowStepDto = S['WorkflowStepDto'];
export type WorkflowEventDto = S['WorkflowEventDto'];
export type WorkflowStatus = S['WorkflowStatus'];
export type WorkflowTemplateDto = S['WorkflowTemplateDto'];
export type SaveWorkflowTemplateRequest = S['SaveWorkflowTemplateRequest'];
export type SaveWorkflowStepRequest = S['SaveWorkflowStepRequest'];
export type WorkflowActionType = S['WorkflowActionType'];
export type AssigneeType = S['AssigneeType'];
export type DocumentType = S['DocumentType'];
export type InboxTab = S['InboxTab'];
export type InboxItemDto = S['InboxItemDto'];
export type InboxCountDto = S['InboxCountDto'];
export type SigningProviderDto = S['SigningProviderDto'];
export type SigningRequestDto = S['SigningRequestDto'];
export type BatchSignResultDto = S['BatchSignResultDto'];
export type VerifyResultDto = S['VerifyResultDto'];
export type PdfSignatureInfo = S['PdfSignatureInfo'];
export type NotificationDto = S['NotificationDto'];
export type NotificationListDto = S['NotificationListDto'];
export type PagedInbox = S['PagedResultOfInboxItemDto'];

const WORKFLOWS = '/api/workflows';
const SIGNING = '/api/signing';
const WF_TEMPLATES = '/api/workflow-templates';
const NOTIFICATIONS = '/api/notifications';

/**
 * A workflow action changes the document and the business record that owns it (contract, proposal, summary...):
 * refresh every active query rather than keeping a list of modules in sync.
 */
export function useInvalidateWorkflow() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries();
}
// ---------------- Workflow on a document ----------------

export function useDocumentWorkflow(documentId: string | undefined) {
  return useQuery({
    queryKey: [WORKFLOWS, 'by-document', documentId],
    queryFn: async () =>
      (await apiClient.get<WorkflowDto | null>(`${WORKFLOWS}/by-document/${documentId}`)).data,
    enabled: !!documentId,
  });
}

export function useStartWorkflow() {
  const invalidate = useInvalidateWorkflow();
  return useMutation({
    mutationFn: async (body: S['StartWorkflowRequest']) =>
      (await apiClient.post<string>(WORKFLOWS, body, { timeout: 180_000 })).data,
    onSuccess: invalidate,
  });
}

export function useApproveStep() {
  const invalidate = useInvalidateWorkflow();
  return useMutation({
    mutationFn: async (v: { stepId: string; comment?: string; skipNextOptional?: boolean }) =>
      (
        await apiClient.post<WorkflowDto>(`${WORKFLOWS}/steps/${v.stepId}/approve`, {
          comment: v.comment ?? null,
          skipNextOptional: v.skipNextOptional ?? false,
        })
      ).data,
    onSuccess: invalidate,
  });
}

export function useReturnStep() {
  const invalidate = useInvalidateWorkflow();
  return useMutation({
    mutationFn: async (v: { stepId: string; toOrder: number; reason: string }) =>
      (
        await apiClient.post<WorkflowDto>(`${WORKFLOWS}/steps/${v.stepId}/return`, {
          toOrder: v.toOrder,
          reason: v.reason,
        })
      ).data,
    onSuccess: invalidate,
  });
}

export function useCommentWorkflow() {
  const invalidate = useInvalidateWorkflow();
  return useMutation({
    mutationFn: async (v: { instanceId: string; text: string }) =>
      (await apiClient.post<WorkflowDto>(`${WORKFLOWS}/${v.instanceId}/comments`, { text: v.text })).data,
    onSuccess: invalidate,
  });
}

export function useResubmitWorkflow() {
  const invalidate = useInvalidateWorkflow();
  return useMutation({
    mutationFn: async (instanceId: string) =>
      (await apiClient.post<WorkflowDto>(`${WORKFLOWS}/${instanceId}/resubmit`)).data,
    onSuccess: invalidate,
  });
}

export function useCancelWorkflow() {
  const invalidate = useInvalidateWorkflow();
  return useMutation({
    mutationFn: async (v: { instanceId: string; reason: string }) => {
      await apiClient.post(`${WORKFLOWS}/${v.instanceId}/cancel`, { text: v.reason });
    },
    onSuccess: invalidate,
  });
}

// ---------------- Trình ký (inbox) and signing ----------------

export interface InboxQuery {
  tab: InboxTab;
  page: number;
  pageSize: number;
  search?: string;
  documentType?: DocumentType;
  supplierId?: string;
  from?: string;
  to?: string;
  /** Only steps of this kind (the "Rà soát" menu uses Review). */
  actionType?: WorkflowActionType;
}

export function useInbox(q: InboxQuery) {
  return useQuery({
    queryKey: [SIGNING, 'inbox', q],
    queryFn: async () => (await apiClient.get<PagedInbox>(`${SIGNING}/inbox`, { params: q })).data,
    placeholderData: (previous) => previous,
  });
}

export function useInboxCount(enabled: boolean) {
  return useQuery({
    queryKey: [SIGNING, 'inbox-count'],
    queryFn: async () => (await apiClient.get<InboxCountDto>(`${SIGNING}/inbox/count`)).data,
    enabled,
    refetchInterval: 120_000,
  });
}

export function useSigningProviders(enabled = true) {
  return useQuery({
    queryKey: [SIGNING, 'providers'],
    queryFn: async () => (await apiClient.get<SigningProviderDto[]>(`${SIGNING}/providers`)).data,
    enabled,
    staleTime: 5 * 60_000,
  });
}

export async function beginSign(
  stepId: string,
  body: { provider?: string; clientCertificate?: string; comment?: string },
): Promise<SigningRequestDto> {
  return (
    await apiClient.post<SigningRequestDto>(
      `${SIGNING}/steps/${stepId}/sign`,
      {
        provider: body.provider ?? null,
        clientCertificate: body.clientCertificate ?? null,
        comment: body.comment ?? null,
      },
      { timeout: 120_000 },
    )
  ).data;
}

export async function getSigningRequest(requestId: string): Promise<SigningRequestDto> {
  return (await apiClient.get<SigningRequestDto>(`${SIGNING}/requests/${requestId}`, { timeout: 60_000 }))
    .data;
}

export async function completeSigningRequest(
  requestId: string,
  signatureValue: string,
): Promise<SigningRequestDto> {
  return (
    await apiClient.post<SigningRequestDto>(
      `${SIGNING}/requests/${requestId}/complete`,
      { signatureValue },
      { timeout: 120_000 },
    )
  ).data;
}

export function useBatchSign() {
  const invalidate = useInvalidateWorkflow();
  return useMutation({
    mutationFn: async (v: { stepIds: string[]; provider?: string }) =>
      (
        await apiClient.post<BatchSignResultDto[]>(
          `${SIGNING}/batch`,
          { stepIds: v.stepIds, provider: v.provider ?? null },
          { timeout: 600_000 },
        )
      ).data,
    onSuccess: invalidate,
  });
}

export async function verifyVersion(documentId: string, versionId: string): Promise<VerifyResultDto> {
  return (
    await apiClient.get<VerifyResultDto>(`${SIGNING}/documents/${documentId}/versions/${versionId}/verify`)
  ).data;
}

export async function verifyUpload(file: File): Promise<VerifyResultDto> {
  const data = new FormData();
  data.append('file', file);
  return (await apiClient.post<VerifyResultDto>(`${SIGNING}/verify`, data, { timeout: 120_000 })).data;
}

// ---------------- Workflow templates (Cấu hình luồng ký) ----------------

export function useWorkflowTemplates(documentType?: DocumentType, enabled = true) {
  return useQuery({
    queryKey: [WF_TEMPLATES, documentType ?? 'all'],
    queryFn: async () =>
      (await apiClient.get<WorkflowTemplateDto[]>(WF_TEMPLATES, { params: { documentType } })).data,
    enabled,
  });
}

export function useSaveWorkflowTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (v: { id?: string; body: SaveWorkflowTemplateRequest }) =>
      v.id
        ? (await apiClient.put<string>(`${WF_TEMPLATES}/${v.id}`, v.body)).data
        : (await apiClient.post<string>(WF_TEMPLATES, v.body)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: [WF_TEMPLATES] }),
  });
}

// ---------------- Notifications ----------------

export function useNotifications(enabled: boolean) {
  return useQuery({
    queryKey: [NOTIFICATIONS],
    queryFn: async () =>
      (await apiClient.get<NotificationListDto>(NOTIFICATIONS, { params: { take: 20 } })).data,
    enabled,
    refetchInterval: 300_000,
  });
}

export function useMarkNotificationsRead() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id?: string) => {
      await apiClient.post(id ? `${NOTIFICATIONS}/${id}/read` : `${NOTIFICATIONS}/read`);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: [NOTIFICATIONS] }),
  });
}
