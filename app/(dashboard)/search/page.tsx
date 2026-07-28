import { Suspense } from 'react';
import type { Metadata } from 'next';

import { SearchView } from '@/components/search/search-view';
import { Skeleton } from '@/components/ui/misc';

export const metadata: Metadata = { title: 'Search Businesses' };

export default function SearchPage() {
  return (
    // useSearchParams needs a Suspense boundary to avoid opting the whole route
    // into client-side rendering.
    <Suspense fallback={<Skeleton className="h-96 w-full" />}>
      <SearchView />
    </Suspense>
  );
}
