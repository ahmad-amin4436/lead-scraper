'use client';

import * as React from 'react';
import { usePathname } from 'next/navigation';
import { Menu } from 'lucide-react';

import { SidebarContent } from '@/components/layout/sidebar';
import { ThemeToggle } from '@/components/layout/theme-toggle';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogTitle, DialogTrigger } from '@/components/ui/dialog';
import { NAV_ITEMS } from '@/lib/constants/navigation';

export function Topbar() {
  const pathname = usePathname();
  const [open, setOpen] = React.useState(false);

  const current =
    NAV_ITEMS.find((item) => pathname === item.href || pathname.startsWith(`${item.href}/`)) ??
    NAV_ITEMS[0];

  return (
    <header className="sticky top-0 z-30 flex h-16 items-center gap-3 border-b border-border bg-background/85 px-4 backdrop-blur-md sm:px-6">
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogTrigger asChild>
          <Button variant="ghost" size="icon" className="lg:hidden" aria-label="Open navigation">
            <Menu />
          </Button>
        </DialogTrigger>
        <DialogContent className="left-0 top-0 h-dvh max-w-72 translate-x-0 translate-y-0 rounded-none p-0">
          <DialogTitle className="sr-only">Navigation</DialogTitle>
          <SidebarContent onNavigate={() => setOpen(false)} />
        </DialogContent>
      </Dialog>

      <div className="min-w-0 flex-1">
        <h1 className="truncate text-base font-semibold tracking-tight">{current.label}</h1>
        <p className="truncate text-xs text-muted-foreground">{current.description}</p>
      </div>

      <ThemeToggle />
    </header>
  );
}
