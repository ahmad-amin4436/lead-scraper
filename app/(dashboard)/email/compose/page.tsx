import type { Metadata } from 'next';

import { EmailComposeView } from '@/components/email/email-compose-view';

export const metadata: Metadata = { title: 'Send Email' };

export default function EmailComposePage() {
  return <EmailComposeView />;
}
