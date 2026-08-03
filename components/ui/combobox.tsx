'use client';

import * as React from 'react';
import * as PopoverPrimitive from '@radix-ui/react-popover';
import { Command } from 'cmdk';
import { Check, ChevronsUpDown, Search, X } from 'lucide-react';

import { cn } from '@/lib/utils';

export interface ComboboxOption {
  value: string;
  label: string;
  /** Extra searchable text (e.g. a state name under a city). */
  hint?: string;
}

interface ComboboxProps {
  options: ComboboxOption[];
  value: string | null;
  onChange: (value: string | null) => void;
  placeholder?: string;
  searchPlaceholder?: string;
  emptyMessage?: string;
  disabled?: boolean;
  clearable?: boolean;
  id?: string;
  className?: string;
  'aria-label'?: string;
}

/**
 * Type-to-filter single select.
 *
 * Built on cmdk rather than a native `<select>` because these lists run to
 * hundreds of entries (250 countries, 200+ cities per state) where scrolling is
 * unusable. Only the visible slice is rendered — see {@link MAX_RENDERED} — so a
 * large list stays responsive while typing.
 */
const MAX_RENDERED = 100;

export function Combobox({
  options,
  value,
  onChange,
  placeholder = 'Select…',
  searchPlaceholder = 'Type to search…',
  emptyMessage = 'No matches.',
  disabled,
  clearable,
  id,
  className,
  'aria-label': ariaLabel,
}: ComboboxProps) {
  const [open, setOpen] = React.useState(false);
  const [query, setQuery] = React.useState('');

  // `combobox` requires aria-controls pointing at the popup it opens, so screen
  // readers can announce the relationship.
  const listboxId = React.useId();

  const selected = React.useMemo(
    () => options.find((option) => option.value === value) ?? null,
    [options, value],
  );

  // cmdk's own filter is O(n) per keystroke over every item; filtering here and
  // capping the result keeps a 200k-city list from janking the input.
  const visible = React.useMemo(() => {
    const needle = query.trim().toLowerCase();

    if (!needle) return options.slice(0, MAX_RENDERED);

    const matches: ComboboxOption[] = [];
    for (const option of options) {
      const haystack = `${option.label} ${option.hint ?? ''}`.toLowerCase();
      if (haystack.includes(needle)) {
        matches.push(option);
        if (matches.length >= MAX_RENDERED) break;
      }
    }
    return matches;
  }, [options, query]);

  const hiddenCount = Math.max(0, options.length - visible.length);

  return (
    <PopoverPrimitive.Root
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) setQuery('');
      }}
    >
      <PopoverPrimitive.Trigger asChild>
        <button
          id={id}
          type="button"
          role="combobox"
          aria-expanded={open}
          aria-controls={listboxId}
          aria-haspopup="listbox"
          aria-label={ariaLabel}
          disabled={disabled}
          className={cn(
            'flex h-9 w-full items-center justify-between gap-2 rounded-md border border-input bg-background px-3 py-2 text-sm shadow-sm',
            'focus:border-ring focus:ring-[3px] focus:ring-ring/25 focus:outline-none',
            'disabled:cursor-not-allowed disabled:opacity-50',
            className,
          )}
        >
          <span className={cn('truncate text-left', !selected && 'text-muted-foreground')}>
            {selected?.label ?? placeholder}
          </span>

          <span className="flex shrink-0 items-center gap-1">
            {clearable && selected && !disabled && (
              <span
                role="button"
                tabIndex={-1}
                aria-label="Clear"
                onClick={(event) => {
                  // Don't let the click open the popover as well.
                  event.stopPropagation();
                  onChange(null);
                }}
                className="rounded p-0.5 text-muted-foreground hover:bg-accent hover:text-foreground"
              >
                <X className="size-3.5" />
              </span>
            )}
            <ChevronsUpDown className="size-4 shrink-0 opacity-60" />
          </span>
        </button>
      </PopoverPrimitive.Trigger>

      <PopoverPrimitive.Portal>
        <PopoverPrimitive.Content
          align="start"
          sideOffset={6}
          className="z-50 w-[var(--radix-popover-trigger-width)] min-w-[220px] overflow-hidden rounded-md border border-border bg-popover text-popover-foreground shadow-lg"
        >
          <Command shouldFilter={false}>
            <div className="flex items-center gap-2 border-b border-border px-3">
              <Search className="size-4 shrink-0 opacity-60" />
              <Command.Input
                autoFocus
                value={query}
                onValueChange={setQuery}
                placeholder={searchPlaceholder}
                className="h-9 w-full bg-transparent text-sm outline-none placeholder:text-muted-foreground"
              />
            </div>

            <Command.List id={listboxId} className="scrollbar-thin max-h-64 overflow-y-auto p-1">
              <Command.Empty className="px-3 py-6 text-center text-sm text-muted-foreground">
                {emptyMessage}
              </Command.Empty>

              {visible.map((option) => (
                <Command.Item
                  key={option.value}
                  value={option.value}
                  onSelect={() => {
                    onChange(option.value);
                    setOpen(false);
                    setQuery('');
                  }}
                  className={cn(
                    'flex cursor-pointer items-center gap-2 rounded-sm px-2 py-1.5 text-sm',
                    'data-[selected=true]:bg-accent data-[selected=true]:text-accent-foreground',
                  )}
                >
                  <Check
                    className={cn(
                      'size-4 shrink-0',
                      option.value === value ? 'opacity-100' : 'opacity-0',
                    )}
                  />
                  <span className="min-w-0 flex-1 truncate">{option.label}</span>
                  {option.hint && (
                    <span className="shrink-0 truncate text-xs text-muted-foreground">
                      {option.hint}
                    </span>
                  )}
                </Command.Item>
              ))}

              {hiddenCount > 0 && (
                <p className="px-3 py-2 text-center text-xs text-muted-foreground">
                  {hiddenCount.toLocaleString()} more — keep typing to narrow it down
                </p>
              )}
            </Command.List>
          </Command>
        </PopoverPrimitive.Content>
      </PopoverPrimitive.Portal>
    </PopoverPrimitive.Root>
  );
}
