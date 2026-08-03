import type { Metadata } from 'next';

import { BackendLeadsView } from '@/components/admin/leads-view';

export const metadata: Metadata = { title: 'Leads' };

/**
 * The lead database, read from SQL Server through the .NET API.
 *
 * This replaced the Excel/blob-backed view: leads live in the database now, and
 * Excel is only ever an export format. The API scopes rows to the signed-in user
 * unless they hold `leads.view-all`, so this same page is a personal list for a
 * normal user and the whole pipeline for a super admin.
 */
export default function LeadsPage() {
  return <BackendLeadsView />;
}
