import {
  Building2,
  Database,
  Download,
  FileClock,
  LayoutDashboard,
  ScrollText,
  Search,
  Settings,
  ShieldCheck,
  Users,
  type LucideIcon,
} from 'lucide-react';

export interface NavItem {
  href: string;
  label: string;
  icon: LucideIcon;
  description: string;
  /** Marks the item as backend/admin territory, grouped under its own heading. */
  section?: 'admin';
}

export const NAV_ITEMS: NavItem[] = [
  {
    href: '/dashboard',
    label: 'Dashboard',
    icon: LayoutDashboard,
    description: 'Pipeline overview and recent activity',
  },
  {
    href: '/search',
    label: 'Search Businesses',
    icon: Search,
    description: 'Discover and enrich new leads',
  },
  {
    href: '/database',
    label: 'Excel Database',
    icon: Database,
    description: 'Browse and manage saved leads',
  },
  {
    href: '/history',
    label: 'Search History',
    icon: FileClock,
    description: 'Past runs, with one-click rerun',
  },
  {
    href: '/export',
    label: 'Export',
    icon: Download,
    description: 'Excel, CSV and CRM-ready files',
  },
  {
    href: '/logs',
    label: 'Logs',
    icon: ScrollText,
    description: 'Run-by-run activity trail',
  },
  {
    href: '/settings',
    label: 'Settings',
    icon: Settings,
    description: 'API keys, limits and crawler behaviour',
  },
  {
    href: '/admin/leads',
    label: 'Leads',
    icon: Building2,
    description: 'Backend business database',
    section: 'admin',
  },
  {
    href: '/admin/users',
    label: 'Users',
    icon: Users,
    description: 'Accounts, roles and access',
    section: 'admin',
  },
  {
    href: '/admin/roles',
    label: 'Roles',
    icon: ShieldCheck,
    description: 'Permissions matrix',
    section: 'admin',
  },
];
