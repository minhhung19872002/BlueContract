import {
  Bell,
  CircleCheck,
  CircleX,
  ClipboardList,
  Clock,
  FileText,
  MessageSquare,
  PenLine,
  ReceiptText,
  ShieldAlert,
  Truck,
  Wallet,
  Warehouse,
  type LucideIcon,
} from 'lucide-react';
import type { Tone } from './StatusTag';

/** Icon and tone of a notification type (backend NotificationTypes). */
const STYLES: Record<string, { icon: LucideIcon; tone: Tone }> = {
  'signing.required': { icon: PenLine, tone: 'primary' },
  'workflow.returned': { icon: CircleX, tone: 'danger' },
  'workflow.completed': { icon: CircleCheck, tone: 'success' },
  'workflow.overdue': { icon: Clock, tone: 'danger' },
  'workflow.comment': { icon: MessageSquare, tone: 'info' },
  'contract.effective': { icon: FileText, tone: 'success' },
  'contract.liquidated': { icon: FileText, tone: 'info' },
  'proposal.submitted': { icon: ClipboardList, tone: 'primary' },
  'summary.approved': { icon: ClipboardList, tone: 'success' },
  'callorder.sent': { icon: Truck, tone: 'warning' },
  'callorder.confirmed': { icon: Truck, tone: 'success' },
  'receipt.completed': { icon: Warehouse, tone: 'success' },
  'receipt.returned': { icon: Warehouse, tone: 'danger' },
  'receipt.invoice': { icon: ReceiptText, tone: 'warning' },
  'acceptance.drafted': { icon: Wallet, tone: 'primary' },
  'payment.completed': { icon: Wallet, tone: 'success' },
  'integrity.alert': { icon: ShieldAlert, tone: 'danger' },
};

export function NotificationIcon({ type, size = 32 }: { type: string; size?: number }) {
  const { icon: Icon, tone } = STYLES[type] ?? { icon: Bell, tone: 'neutral' as Tone };
  return (
    <span
      className="notification-icon"
      style={{ width: size, height: size, background: `var(--bp-${tone}-bg)`, color: `var(--bp-${tone}-fg)` }}
      aria-hidden
    >
      <Icon size={16} strokeWidth={1.75} />
    </span>
  );
}
