import { expect, test } from '@playwright/test';
import { apiAs, loginAs } from './helpers';

/**
 * AUDIT-1 §13.2 / spec §11 "API stateless, chạy nhiều instance": with `docker compose up --scale api=2` (nginx spreads
 * requests over the instances) a user connected to one instance still receives every realtime notification sent by
 * the others, and a remote signing session started on one instance completes when polled on another. Passes on a
 * single instance too; run it scaled to check the Redis backplane and shared cache (scripts/scale-check).
 */
const ROUNDS = 4;

test('thông báo realtime và phiên ký đi qua mọi instance API', async ({ browser, baseURL }) => {
  const storekeeper = await loginAs(browser, 'thukho');
  await storekeeper.goto('/trinh-ky');
  const bell = storekeeper.getByRole('button', { name: /^Thông báo/ });
  const unread = async () => Number(/(\d+) chưa đọc/.exec((await bell.getAttribute('aria-label')) ?? '')?.[1] ?? 0);
  const before = await unread();

  // The administrator starts ROUNDS sample workflows; each waits for the storekeeper (one realtime push each).
  const admin = await apiAs(baseURL!, 'admin');
  const templates = (await (await admin.ctx.get('/api/templates', { headers: admin.headers })).json()) as { id: string; code: string }[];
  const workflows = (await (await admin.ctx.get('/api/workflow-templates', { headers: admin.headers })).json()) as { id: string; code: string }[];
  const suppliers = (await (await admin.ctx.get('/api/lookups/suppliers', { headers: admin.headers })).json()) as { id: string; code: string }[];
  const documents: string[] = [];
  for (let i = 0; i < ROUNDS; i++) {
    const doc = await admin.ctx.post('/api/documents/samples', {
      headers: admin.headers,
      data: { templateId: templates.find((t) => t.code === 'PHIEU_NHAP_KHO')!.id, title: null },
    });
    expect(doc.ok(), await doc.text()).toBeTruthy();
    const documentId = (await doc.json()) as string;
    const started = await admin.ctx.post('/api/workflows', {
      headers: admin.headers,
      data: {
        documentId,
        templateId: workflows.find((w) => w.code === 'WF_SAMPLE')!.id,
        supplierId: suppliers.find((s) => s.code === '0101234567')!.id,
        facilityId: null,
        domain: null,
      },
    });
    expect(started.ok(), await started.text()).toBeTruthy();
    documents.push(documentId);
  }

  // Every push arrives without reloading the page, whichever instance sent it.
  await expect.poll(unread, { timeout: 30_000, message: 'realtime notifications lost between API instances' }).toBeGreaterThanOrEqual(
    before + ROUNDS,
  );

  // Remote signing: begin on one request, poll on others until the (simulated) phone confirmation completes.
  const signer = await apiAs(baseURL!, 'thukho');
  const inbox = (await (
    await signer.ctx.get('/api/signing/inbox?tab=Pending&pageSize=100', { headers: signer.headers })
  ).json()) as { items: { documentId: string; stepId: string }[] };
  for (const documentId of documents) {
    const step = inbox.items.find((i) => i.documentId === documentId)!.stepId;
    const begun = await signer.ctx.post(`/api/signing/steps/${step}/sign`, {
      headers: signer.headers,
      data: { provider: 'MOCK_REMOTE', clientCertificate: null, comment: null },
    });
    expect(begun.ok(), await begun.text()).toBeTruthy();
    const { requestId } = (await begun.json()) as { requestId: string };
    await expect
      .poll(
        async () =>
          ((await (await signer.ctx.get(`/api/signing/requests/${requestId}`, { headers: signer.headers })).json()) as {
            status: string;
          }).status,
        { timeout: 60_000, intervals: [500, 1000, 1000, 2000], message: 'signing session lost between API instances' },
      )
      .toBe('Completed');
  }
});
