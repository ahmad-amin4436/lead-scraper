import type { Metadata } from 'next';

import { LinkedInEnrichmentView } from '@/components/linkedin/linkedin-enrichment-view';

export const metadata: Metadata = { title: 'LinkedIn Enrichment' };

export default function LinkedInEnrichmentPage() {
  return <LinkedInEnrichmentView />;
}
