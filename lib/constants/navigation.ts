import {
  Database,
  Download,
  FileClock,
  LayoutDashboard,
  ScrollText,
  Search,
  Settings,
  type LucideIcon,
} from 'lucide-react';

export interface NavItem {
  href: string;
  label: string;
  icon: LucideIcon;
  description: string;
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
];
