import type { Metadata } from 'next';

import { WhatsAppTemplatesView } from '@/components/whatsapp/whatsapp-templates-view';

export const metadata: Metadata = { title: 'WhatsApp Presets' };

export default function WhatsAppTemplatesPage() {
  return <WhatsAppTemplatesView />;
}
