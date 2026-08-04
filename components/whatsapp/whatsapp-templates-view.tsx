'use client';

import * as React from 'react';
import { MessageCircle, Pencil, Plus, Trash2 } from 'lucide-react';
import { toast } from 'sonner';

import { PageHeader } from '@/components/shared/page-header';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input, Textarea } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Alert, AlertDescription, EmptyState, Separator, Skeleton } from '@/components/ui/misc';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Switch } from '@/components/ui/switch';
import {
  useDeleteWhatsAppTemplate,
  useSaveWhatsAppTemplate,
  useWhatsAppTemplates,
  type WhatsAppTemplate,
} from '@/hooks/use-whatsapp';
import { formatDate, formatNumber } from '@/utils/format';

/** Kept in step with TemplateRenderer.AvailableTokens on the backend (minus sender_email — a click-to-chat message has no "from" address). */
const TOKENS = [
  '{{business_name}}',
  '{{category}}',
  '{{city}}',
  '{{state}}',
  '{{country}}',
  '{{address}}',
  '{{phone}}',
  '{{email}}',
  '{{website}}',
  '{{sender_name}}',
];

interface DraftTemplate {
  id?: string;
  name: string;
  description: string;
  message: string;
  isActive: boolean;
}

const EMPTY_TEMPLATE: DraftTemplate = {
  name: '',
  description: '',
  message: 'Hi {{business_name}}, this is {{sender_name}}.',
  isActive: true,
};

/** Admin screen for the WhatsApp click-to-chat presets users pick from. */
export function WhatsAppTemplatesView() {
  const templates = useWhatsAppTemplates();
  const saveTemplate = useSaveWhatsAppTemplate();
  const deleteTemplate = useDeleteWhatsAppTemplate();

  const [draft, setDraft] = React.useState<DraftTemplate | null>(null);
  const [confirmDelete, setConfirmDelete] = React.useState<WhatsAppTemplate | null>(null);

  const openNew = (): void => setDraft({ ...EMPTY_TEMPLATE });

  const openEdit = (template: WhatsAppTemplate): void =>
    setDraft({
      id: template.id,
      name: template.name,
      description: template.description,
      message: template.message,
      isActive: template.isActive,
    });

  const submit = (): void => {
    if (!draft) return;

    saveTemplate.mutate(
      {
        id: draft.id,
        body: {
          name: draft.name,
          description: draft.description,
          message: draft.message,
          isActive: draft.isActive,
        },
      },
      {
        onSuccess: () => {
          toast.success(draft.id ? 'Preset updated.' : 'Preset created.');
          setDraft(null);
        },
        onError: (error) => toast.error(error.message),
      },
    );
  };

  const rows = templates.data ?? [];

  return (
    <>
      <PageHeader
        title="WhatsApp presets"
        description="Messages your team can open in WhatsApp for a lead. Users cannot compose freely."
        actions={
          <Button onClick={openNew}>
            <Plus />
            New preset
          </Button>
        }
      />

      {templates.isError && (
        <Alert variant="destructive" className="mb-6">
          <AlertDescription>{templates.error.message}</AlertDescription>
        </Alert>
      )}

      <Card>
        {templates.isPending ? (
          <CardContent className="space-y-2 p-5">
            {Array.from({ length: 4 }, (_, index) => (
              <Skeleton key={index} className="h-12 w-full" />
            ))}
          </CardContent>
        ) : rows.length === 0 ? (
          <CardContent className="p-5">
            <EmptyState
              icon={<MessageCircle />}
              title="No presets yet"
              description="Create one so your team has an approved message to open in WhatsApp."
              action={
                <Button size="sm" onClick={openNew}>
                  Create the first preset
                </Button>
              }
            />
          </CardContent>
        ) : (
          <TableContainer>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="pl-5">Preset</TableHead>
                  <TableHead>Message</TableHead>
                  <TableHead>Used</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead className="pr-5 text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((template) => (
                  <TableRow key={template.id}>
                    <TableCell className="pl-5">
                      <p className="max-w-[200px] truncate font-medium">{template.name}</p>
                      <p className="max-w-[200px] truncate text-xs text-muted-foreground">
                        {template.description || `Created ${formatDate(template.createdAt)}`}
                      </p>
                    </TableCell>
                    <TableCell className="max-w-[280px] truncate text-sm">
                      {template.message}
                    </TableCell>
                    <TableCell className="tabular-nums">
                      {formatNumber(template.timesUsed)}
                    </TableCell>
                    <TableCell>
                      <Badge variant={template.isActive ? 'success' : 'muted'}>
                        {template.isActive ? 'Active' : 'Retired'}
                      </Badge>
                    </TableCell>
                    <TableCell className="pr-5">
                      <div className="flex justify-end gap-1">
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          onClick={() => openEdit(template)}
                          aria-label="Edit preset"
                        >
                          <Pencil />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          onClick={() => setConfirmDelete(template)}
                          aria-label="Delete preset"
                        >
                          <Trash2 />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </Card>

      {/* --- preset editor --- */}
      <Dialog open={draft !== null} onOpenChange={(open) => !open && setDraft(null)}>
        <DialogContent className="max-w-2xl">
          <DialogHeader>
            <DialogTitle>{draft?.id ? 'Edit preset' : 'New preset'}</DialogTitle>
            <DialogDescription>
              Placeholders are replaced per recipient when a link is generated.
            </DialogDescription>
          </DialogHeader>

          {draft && (
            <div className="space-y-4">
              <div className="space-y-1.5">
                <Label htmlFor="tplName">Name</Label>
                <Input
                  id="tplName"
                  value={draft.name}
                  onChange={(event) => setDraft({ ...draft, name: event.target.value })}
                  placeholder="Intro outreach"
                />
              </div>

              <div className="space-y-1.5">
                <Label htmlFor="tplDescription">Description</Label>
                <Input
                  id="tplDescription"
                  value={draft.description}
                  onChange={(event) => setDraft({ ...draft, description: event.target.value })}
                  placeholder="When should this be used?"
                />
              </div>

              <div className="space-y-1.5">
                <Label htmlFor="tplMessage">Message</Label>
                <Textarea
                  id="tplMessage"
                  value={draft.message}
                  onChange={(event) => setDraft({ ...draft, message: event.target.value })}
                  className="min-h-[140px] font-mono text-xs"
                />
                <div className="flex flex-wrap gap-1 pt-1">
                  {TOKENS.map((token) => (
                    <button
                      key={token}
                      type="button"
                      onClick={() => setDraft({ ...draft, message: draft.message + token })}
                      className="rounded-full border border-dashed border-border px-2 py-0.5 font-mono text-[11px] text-muted-foreground transition-colors hover:border-primary hover:text-primary"
                    >
                      {token}
                    </button>
                  ))}
                </div>
              </div>

              <Separator />

              <label className="flex cursor-pointer items-center justify-between gap-4">
                <span className="text-sm">
                  Active
                  <span className="block text-xs text-muted-foreground">
                    Retired presets stay in the history but cannot be used.
                  </span>
                </span>
                <Switch
                  checked={draft.isActive}
                  onCheckedChange={(checked) => setDraft({ ...draft, isActive: checked })}
                />
              </label>
            </div>
          )}

          <DialogFooter>
            <Button variant="outline" onClick={() => setDraft(null)}>
              Cancel
            </Button>
            <Button
              onClick={submit}
              loading={saveTemplate.isPending}
              disabled={!draft?.name.trim() || !draft?.message.trim()}
            >
              {draft?.id ? 'Save changes' : 'Create preset'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* --- delete confirm --- */}
      <Dialog open={confirmDelete !== null} onOpenChange={(open) => !open && setConfirmDelete(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete “{confirmDelete?.name}”?</DialogTitle>
            <DialogDescription>
              {(confirmDelete?.timesUsed ?? 0) > 0
                ? `This preset has been used ${formatNumber(confirmDelete?.timesUsed ?? 0)} time(s), so it will be retired rather than deleted — the contact history stays intact.`
                : 'This preset has never been used, so it will be removed permanently.'}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmDelete(null)}>
              Cancel
            </Button>
            <Button
              variant="destructive"
              loading={deleteTemplate.isPending}
              onClick={() => {
                if (!confirmDelete) return;

                deleteTemplate.mutate(confirmDelete.id, {
                  onSuccess: () => {
                    toast.success('Preset removed.');
                    setConfirmDelete(null);
                  },
                  onError: (error) => toast.error(error.message),
                });
              }}
            >
              {(confirmDelete?.timesUsed ?? 0) > 0 ? 'Retire preset' : 'Delete preset'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
