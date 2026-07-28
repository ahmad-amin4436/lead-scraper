import * as React from 'react';
import type { LucideIcon } from 'lucide-react';

import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/misc';
import { cn } from '@/lib/utils';

interface StatCardProps {
  label: string;
  value: string | number;
  hint?: string;
  icon: LucideIcon;
  /** Semantic accent for the icon chip. */
  tone?: 'primary' | 'success' | 'warning' | 'muted';
  loading?: boolean;
}

const TONES: Record<NonNullable<StatCardProps['tone']>, string> = {
  primary: 'bg-primary/12 text-primary',
  success: 'bg-success/15 text-success',
  warning: 'bg-warning/18 text-warning',
  muted: 'bg-muted text-muted-foreground',
};

export function StatCard({
  label,
  value,
  hint,
  icon: Icon,
  tone = 'primary',
  loading = false,
}: StatCardProps) {
  return (
    <Card className="p-5">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 space-y-1">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
            {label}
          </p>
          {loading ? (
            <Skeleton className="h-8 w-20" />
          ) : (
            <p className="text-2xl font-semibold tabular-nums tracking-tight">{value}</p>
          )}
          {hint && !loading && (
            <p className="truncate text-xs text-muted-foreground">{hint}</p>
          )}
        </div>
        <span
          className={cn('flex size-9 shrink-0 items-center justify-center rounded-lg', TONES[tone])}
        >
          <Icon className="size-4" />
        </span>
      </div>
    </Card>
  );
}
