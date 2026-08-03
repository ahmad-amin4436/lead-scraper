import type { Metadata } from 'next';

import { EmailLogView } from '@/components/email/email-log-view';

export const metadata: Metadata = { title: 'All Email Activity' };

/**
 * Admin-wide view. Visibility is decided by the API from the caller's
 * permissions, so a user who reaches this URL without `email.view-all-logs`
 * still only sees their own rows.
 */
export default function AdminEmailLogPage() {
  return <EmailLogView scope="all" />;
}
