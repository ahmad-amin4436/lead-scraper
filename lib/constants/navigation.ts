import {
  Building2,
  Contact2,
  Database,
  Download,
  FileClock,
  LayoutDashboard,
  Mail,
  MailCheck,
  MessageCircle,
  ScrollText,
  Search,
  Send,
  Settings,
  ShieldCheck,
  UserSearch,
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
  /**
   * Permission required to see the item. Undefined means everyone signed in.
   * The sidebar hides what the user cannot use, and the API enforces the same
   * rule — hiding a link is presentation, not access control.
   */
  permission?: string;
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
    permission: 'searches.create',
  },
  {
    href: '/database',
    label: 'Leads',
    icon: Database,
    // Renamed from "Excel Database": leads live in SQL Server now, and Excel is
    // only ever an export format.
    description: 'Your saved leads from the database',
    permission: 'leads.view',
  },
  {
    href: '/people',
    label: 'People',
    icon: UserSearch,
    description: 'Decision-makers found via LinkedIn search',
    permission: 'people.view',
  },
  {
    href: '/linkedin',
    label: 'LinkedIn Enrichment',
    icon: Contact2,
    description: 'Add company detail and find decision-makers for leads you have',
    permission: 'people.manage',
  },
  {
    href: '/people-search',
    label: 'LinkedIn People Search',
    icon: Building2,
    description: 'Search one company’s LinkedIn People tab and save every match',
    permission: 'people.manage',
  },
  {
    href: '/email/compose',
    label: 'Send Email',
    icon: Send,
    description: 'Email leads using an approved preset',
    permission: 'email.send',
  },
  {
    href: '/email/history',
    label: 'Email History',
    icon: MailCheck,
    description: 'What was sent, to whom and when',
    permission: 'email.view-own-log',
  },
  {
    href: '/whatsapp/compose',
    label: 'Send WhatsApp',
    icon: MessageCircle,
    description: 'Open click-to-chat links using an approved preset',
    permission: 'whatsapp.send',
  },
  {
    href: '/history',
    label: 'Search History',
    icon: FileClock,
    description: 'Past runs, with one-click rerun',
    permission: 'searches.view',
  },
  {
    href: '/export',
    label: 'Export',
    icon: Download,
    description: 'Excel, CSV and CRM-ready files',
    permission: 'leads.export',
  },
  {
    href: '/logs',
    label: 'Logs',
    icon: ScrollText,
    description: 'Run-by-run activity trail',
    permission: 'system.view-logs',
  },
  {
    href: '/settings',
    label: 'Settings',
    icon: Settings,
    description: 'Limits and crawler behaviour',
    permission: 'settings.view',
  },
  {
    href: '/admin/email-templates',
    label: 'Email Presets',
    icon: Mail,
    description: 'Templates and signatures users send from',
    section: 'admin',
    permission: 'email.manage-templates',
  },
  {
    href: '/admin/email-log',
    label: 'All Email Activity',
    icon: MailCheck,
    description: 'Every user’s sends, filterable by date',
    section: 'admin',
    permission: 'email.view-all-logs',
  },
  {
    href: '/admin/whatsapp-templates',
    label: 'WhatsApp Presets',
    icon: MessageCircle,
    description: 'Click-to-chat messages users open leads with',
    section: 'admin',
    permission: 'whatsapp.manage-templates',
  },
  {
    href: '/admin/users',
    label: 'Users',
    icon: Users,
    description: 'Accounts, roles and access',
    section: 'admin',
    permission: 'users.view',
  },
  {
    href: '/admin/roles',
    label: 'Roles',
    icon: ShieldCheck,
    description: 'Permissions matrix',
    section: 'admin',
    permission: 'roles.view',
  },
];
