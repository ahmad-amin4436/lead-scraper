import type { Metadata } from 'next';

import { EmailLogView } from '@/components/email/email-log-view';

export const metadata: Metadata = { title: 'Email History' };

export default function EmailHistoryPage() {
  return <EmailLogView scope="mine" />;
}
