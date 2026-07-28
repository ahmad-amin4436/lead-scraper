import type { Metadata } from 'next';

import { HistoryView } from '@/components/history/history-view';

export const metadata: Metadata = { title: 'Search History' };

export default function HistoryPage() {
  return <HistoryView />;
}
