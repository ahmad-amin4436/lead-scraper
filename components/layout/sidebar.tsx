'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { Gem, LogIn, LogOut } from 'lucide-react';

import { useAuth } from '@/components/providers/auth-provider';
import { hasPermission } from '@/hooks/use-auth';
import { Button } from '@/components/ui/button';
import { NAV_ITEMS } from '@/lib/constants/navigation';
import { cn } from '@/lib/utils';

interface SidebarProps {
  /** Called after a navigation link is chosen, so the mobile drawer can close. */
  onNavigate?: () => void;
}

export function SidebarContent({ onNavigate }: SidebarProps) {
  const pathname = usePathname();
  const { user, signOut } = useAuth();

  // Hide what the caller cannot use. This is presentation only — every route is
  // independently enforced by the API, so a hidden link is not a security
  // boundary, just a tidier menu.
  const visible = NAV_ITEMS.filter(
    (item) => !item.permission || hasPermission(user, item.permission),
  );

  const adminItems = visible.filter((item) => item.section === 'admin');
  const mainItems = visible.filter((item) => item.section !== 'admin');

  const renderItems = (items: typeof NAV_ITEMS) =>
    items.map((item) => {
      // Highlight nested routes too, without matching every path on "/".
      const active = pathname === item.href || pathname.startsWith(`${item.href}/`);
      const Icon = item.icon;

      return (
        <Link
          key={item.href}
          href={item.href}
          onClick={onNavigate}
          aria-current={active ? 'page' : undefined}
          className={cn(
            'group flex items-start gap-3 rounded-lg px-3 py-2.5 text-sm transition-colors',
            active
              ? 'bg-sidebar-accent font-medium text-sidebar-accent-foreground'
              : 'text-muted-foreground hover:bg-sidebar-accent/60 hover:text-sidebar-foreground',
          )}
        >
          <Icon className={cn('mt-0.5 size-4 shrink-0', active && 'text-primary')} />
          <span className="min-w-0">
            <span className="block truncate">{item.label}</span>
            <span className="block truncate text-[11px] text-muted-foreground/80">
              {item.description}
            </span>
          </span>
        </Link>
      );
    });

  return (
    <div className="flex h-full flex-col gap-2 bg-sidebar text-sidebar-foreground">
      <div className="flex h-16 shrink-0 items-center gap-2.5 border-b border-sidebar-border px-5">
        <span className="flex size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground">
          <Gem className="size-4" />
        </span>
        <div className="leading-tight">
          <p className="text-sm font-semibold tracking-tight">LeadMine AI</p>
          <p className="text-[11px] text-muted-foreground">Lead generation suite</p>
        </div>
      </div>

      <nav className="scrollbar-thin flex-1 space-y-1 overflow-y-auto px-3 py-2">
        {renderItems(mainItems)}

        {adminItems.length > 0 && (
          <>
            <p className="px-3 pt-4 pb-1 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
              Backend
            </p>
            {renderItems(adminItems)}
          </>
        )}
      </nav>

      <div className="border-t border-sidebar-border p-3">
        {user ? (
          <div className="flex items-center gap-2">
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium">{user.fullName}</p>
              <p className="truncate text-[11px] text-muted-foreground">{user.email}</p>
            </div>
            <Button variant="ghost" size="icon-sm" onClick={() => void signOut()} aria-label="Sign out">
              <LogOut />
            </Button>
          </div>
        ) : (
          <Button asChild variant="outline" className="w-full">
            <Link href="/login" onClick={onNavigate}>
              <LogIn />
              Sign in to backend
            </Link>
          </Button>
        )}

        <p className="mt-3 text-[11px] leading-relaxed text-muted-foreground">
          Only publicly listed contact details are collected. robots.txt is respected on every crawl.
        </p>
      </div>
    </div>
  );
}

export function Sidebar() {
  return (
    <aside className="hidden w-72 shrink-0 border-r border-sidebar-border lg:block">
      <div className="sticky top-0 h-dvh">
        <SidebarContent />
      </div>
    </aside>
  );
}
