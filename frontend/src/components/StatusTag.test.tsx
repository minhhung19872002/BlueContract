import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { StatusTag, statusLabel, statusOptions, statusTone } from './StatusTag';

const TONES = [
  'primary',
  'success',
  'warning',
  'orange',
  'danger',
  'info',
  'purple',
  'indigo',
  'neutral',
  'teal',
];

describe('StatusTag (design system tones)', () => {
  it('keeps the Vietnamese labels and gives every status a known tone', () => {
    expect(statusLabel('contractStatus', 'Effective')).toBe('Đang hiệu lực');
    for (const domain of [
      'contractStatus',
      'receiptStatus',
      'receiptSource',
      'workflowAction',
      'callOrderStatus',
    ] as const) {
      for (const { value } of statusOptions(domain)) {
        expect(TONES).toContain(statusTone(domain, value));
      }
    }
  });

  it('applies the tone exceptions of the theme guide', () => {
    expect(statusTone('procurementType', 'NonBidding')).toBe('purple');
    expect(statusTone('receiptSource', 'His')).toBe('teal');
    expect(statusTone('contractStatus', 'Liquidated')).toBe('info');
    expect(statusTone('flag', 'Overdue')).toBe('danger');
  });

  it('shows a dot for statuses, a pulse while running, none for categories and a dashed virtual receipt', () => {
    const running = renderToStaticMarkup(<StatusTag domain="receiptStatus" value="Signing" suffix=" 3/5" />);
    expect(running).toContain('status-tag--running');
    expect(running).toContain('status-tag__dot');
    expect(running).toContain('Đang ký 3/5');

    const category = renderToStaticMarkup(<StatusTag domain="procurementType" value="Bidding" />);
    expect(category).not.toContain('status-tag__dot');

    expect(renderToStaticMarkup(<StatusTag domain="receiptSource" value="VirtualPortal" />)).toContain(
      'status-tag--dashed',
    );
  });
});

describe('StatusTag covers every value the API sends', () => {
  // Domain of the tag → enum schema of the OpenAPI document generated from the backend.
  const SCHEMAS: Record<string, string> = {
    userStatus: 'UserStatus',
    userType: 'UserType',
    departmentType: 'DepartmentType',
    warehouseType: 'WarehouseType',
    templateKind: 'TemplateKind',
    documentStatus: 'DocumentStatus',
    documentType: 'DocumentType',
    bidStatus: 'BidDecisionStatus',
    itemSource: 'ItemSource',
    contractStatus: 'ContractStatus',
    procurementType: 'ProcurementType',
    contractKind: 'ContractKind',
    appendixType: 'AppendixType',
    appendixStatus: 'AppendixStatus',
    revisionKind: 'RevisionKind',
    proposalStatus: 'ProposalStatus',
    summaryStatus: 'ProposalSummaryStatus',
    callOrderStatus: 'CallOrderStatus',
    workflowStatus: 'WorkflowStatus',
    stepStatus: 'StepStatus',
    workflowAction: 'WorkflowActionType',
    assigneeType: 'AssigneeType',
    workflowEvent: 'WorkflowEventType',
    signatureKind: 'SignatureKind',
    historyChange: 'HistoryChangeType',
    receiptStatus: 'ReceiptStatus',
    receiptValidation: 'ReceiptValidationStatus',
    receiptType: 'ReceiptType',
    receiptSource: 'ReceiptSource',
    integrationStatus: 'IntegrationLogStatus',
    paymentRecordStatus: 'PaymentRecordStatus',
    paymentStatus: 'PaymentStatus',
    integrationDirection: 'IntegrationDirection',
  };

  it('has a Vietnamese label for every enum value of the API', async () => {
    const openapi = (await import('../../openapi/blueprocure.json')).default as unknown as {
      components: { schemas: Record<string, { enum?: string[] }> };
    };
    const missing: string[] = [];
    for (const [domain, schema] of Object.entries(SCHEMAS)) {
      const values = openapi.components.schemas[schema]?.enum;
      expect(values, schema).toBeDefined();
      for (const v of values ?? []) {
        const label = statusLabel(domain as Parameters<typeof statusLabel>[0], v);
        if (label === 'Không xác định') missing.push(`${domain}.${v}`);
      }
    }
    expect(missing).toEqual([]);
  });

  it('never shows a raw code for an unknown value', () => {
    expect(statusLabel('contractStatus', 'SomethingNew')).toBe('Không xác định');
  });
});
