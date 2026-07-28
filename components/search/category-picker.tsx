'use client';

import * as React from 'react';
import { Check, Search, X } from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { CATEGORIES_BY_GROUP, getCategoryLabel } from '@/lib/constants/categories';
import { cn } from '@/lib/utils';

interface CategoryPickerProps {
  value: string[];
  onChange: (next: string[]) => void;
  max?: number;
  disabled?: boolean;
}

export function CategoryPicker({ value, onChange, max = 20, disabled }: CategoryPickerProps) {
  const [filter, setFilter] = React.useState('');
  const selected = React.useMemo(() => new Set(value), [value]);

  const groups = React.useMemo(() => {
    const needle = filter.trim().toLowerCase();
    if (!needle) return CATEGORIES_BY_GROUP;

    return CATEGORIES_BY_GROUP.map((group) => ({
      ...group,
      categories: group.categories.filter(
        (category) =>
          category.label.toLowerCase().includes(needle) ||
          group.group.toLowerCase().includes(needle),
      ),
    })).filter((group) => group.categories.length > 0);
  }, [filter]);

  const toggle = (id: string): void => {
    if (selected.has(id)) {
      onChange(value.filter((item) => item !== id));
    } else if (value.length < max) {
      onChange([...value, id]);
    }
  };

  const atLimit = value.length >= max;

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <div className="relative min-w-[200px] flex-1">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={filter}
            onChange={(event) => setFilter(event.target.value)}
            placeholder="Filter categories…"
            className="pl-8"
            disabled={disabled}
            aria-label="Filter categories"
          />
        </div>
        <span className="text-xs tabular-nums text-muted-foreground">
          {value.length} / {max} selected
        </span>
        {value.length > 0 && (
          <Button type="button" variant="ghost" size="sm" onClick={() => onChange([])} disabled={disabled}>
            Clear
          </Button>
        )}
      </div>

      {value.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {value.map((id) => (
            <Badge key={id} variant="default" className="pr-1">
              {getCategoryLabel(id)}
              <button
                type="button"
                onClick={() => toggle(id)}
                disabled={disabled}
                className="ml-0.5 rounded-full p-0.5 hover:bg-primary/20"
                aria-label={`Remove ${getCategoryLabel(id)}`}
              >
                <X className="size-3" />
              </button>
            </Badge>
          ))}
        </div>
      )}

      <div className="scrollbar-thin max-h-72 overflow-y-auto rounded-lg border border-border">
        {groups.length === 0 ? (
          <p className="p-4 text-sm text-muted-foreground">No categories match “{filter}”.</p>
        ) : (
          groups.map((group) => (
            <div key={group.group} className="border-b border-border last:border-0">
              <p className="sticky top-0 z-10 bg-muted/85 px-3 py-1.5 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground backdrop-blur">
                {group.group}
              </p>
              <div className="grid gap-0.5 p-1.5 sm:grid-cols-2">
                {group.categories.map((category) => {
                  const isSelected = selected.has(category.id);
                  // Block new selections at the cap, but always allow deselect.
                  const isDisabled = disabled || (atLimit && !isSelected);

                  return (
                    <button
                      key={category.id}
                      type="button"
                      onClick={() => toggle(category.id)}
                      disabled={isDisabled}
                      aria-pressed={isSelected}
                      className={cn(
                        'flex items-center gap-2 rounded-md px-2.5 py-1.5 text-left text-sm transition-colors',
                        isSelected
                          ? 'bg-primary/12 font-medium text-primary'
                          : 'hover:bg-accent hover:text-accent-foreground',
                        isDisabled && !isSelected && 'cursor-not-allowed opacity-40',
                      )}
                    >
                      <span
                        className={cn(
                          'flex size-4 shrink-0 items-center justify-center rounded border',
                          isSelected ? 'border-primary bg-primary text-primary-foreground' : 'border-input',
                        )}
                      >
                        {isSelected && <Check className="size-3" />}
                      </span>
                      <span className="truncate">{category.label}</span>
                    </button>
                  );
                })}
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
