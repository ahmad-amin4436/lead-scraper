import type { Metadata } from 'next';

import { PeopleSearchView } from '@/components/linkedin/people-search-view';

export const metadata: Metadata = { title: 'LinkedIn People Search' };

export default function PeopleSearchPage() {
  return <PeopleSearchView />;
}
