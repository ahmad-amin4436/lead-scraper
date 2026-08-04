import { Suspense } from 'react';
import type { Metadata } from 'next';

import { PeopleView } from '@/components/people/people-view';
import { Skeleton } from '@/components/ui/misc';

export const metadata: Metadata = { title: 'People' };

export default function PeoplePage() {
  // useSearchParams (for the ?businessId= filter) needs a Suspense boundary to
  // avoid opting the whole route into client-side rendering.
  return (
    <Suspense fallback={<Skeleton className="h-96 w-full" />}>
      <PeopleView />
    </Suspense>
  );
}
