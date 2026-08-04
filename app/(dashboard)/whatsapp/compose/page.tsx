import type { Metadata } from 'next';

import { WhatsAppComposeView } from '@/components/whatsapp/whatsapp-compose-view';

export const metadata: Metadata = { title: 'Send WhatsApp' };

export default function WhatsAppComposePage() {
  return <WhatsAppComposeView />;
}
