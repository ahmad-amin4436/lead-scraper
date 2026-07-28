import type { Metadata } from 'next';

import { DatabaseView } from '@/components/database/database-view';

export const metadata: Metadata = { title: 'Excel Database' };

export default function DatabasePage() {
  return <DatabaseView />;
}
