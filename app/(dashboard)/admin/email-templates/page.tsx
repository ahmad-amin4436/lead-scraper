import type { Metadata } from 'next';

import { EmailTemplatesView } from '@/components/email/email-templates-view';

export const metadata: Metadata = { title: 'Email Presets' };

export default function EmailTemplatesPage() {
  return <EmailTemplatesView />;
}
