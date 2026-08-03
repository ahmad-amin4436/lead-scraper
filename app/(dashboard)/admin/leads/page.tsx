import type { Metadata } from 'next';

import { BackendLeadsView } from '@/components/admin/leads-view';

export const metadata: Metadata = { title: 'Leads' };

export default function AdminLeadsPage() {
  return <BackendLeadsView />;
}
