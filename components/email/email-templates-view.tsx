'use client';

import * as React from 'react';
import { Mail, Pencil, Plus, Trash2 } from 'lucide-react';
import { toast } from 'sonner';

import { PageHeader } from '@/components/shared/page-header';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Switch } from '@/components/ui/switch';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  useDeleteEmailTemplate,
  useEmailSignatures,
  useEmailTemplates,
  useSaveEmailSignature,
  useSaveEmailTemplate,
  type EmailTemplate,
} from '@/hooks/use-email';
import { formatDate, formatNumber } from '@/utils/format';

const NO_SIGNATURE = '__none__';

/** Kept in step with TemplateRenderer.AvailableTokens on the backend. */
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
  '{{sender_email}}',
];

interface DraftTemplate {
  id?: string;
  name: string;
  description: string;
  subject: string;
  bodyHtml: string;
  bodyText: string;
  isActive: boolean;
  signatureId: string;
}

const EMPTY_TEMPLATE: DraftTemplate = {
  name: '',
  description: '',
  subject: '',
  bodyHtml: '<p>Hi {{business_name}},</p>\n<p></p>\n<p>Kind regards,<br />{{sender_name}}</p>',
  bodyText: '',
  isActive: true,
  signatureId: NO_SIGNATURE,
};

/** Admin screen for the presets users send from, plus shared signatures. */
export function EmailTemplatesView() {
  const templates = useEmailTemplates();
  const signatures = useEmailSignatures();
  const saveTemplate = useSaveEmailTemplate();
  const deleteTemplate = useDeleteEmailTemplate();
  const saveSignature = useSaveEmailSignature();

  const [draft, setDraft] = React.useState<DraftTemplate | null>(null);
  const [confirmDelete, setConfirmDelete] = React.useState<EmailTemplate | null>(null);
  const [signatureDraft, setSignatureDraft] = React.useState<{
    id?: string;
    name: string;
    bodyHtml: string;
    isDefault: boolean;
  } | null>(null);

  const openNew = (): void => setDraft({ ...EMPTY_TEMPLATE });

  const openEdit = (template: EmailTemplate): void =>
    setDraft({
      id: template.id,
      name: template.name,
      description: template.description,
      subject: template.subject,
      bodyHtml: template.bodyHtml,
      bodyText: template.bodyText,
      isActive: template.isActive,
      signatureId: template.signatureId ?? NO_SIGNATURE,
    });

  const submit = (): void => {
    if (!draft) return;

    saveTemplate.mutate(
      {
        id: draft.id,
        body: {
          name: draft.name,
          description: draft.description,
          subject: draft.subject,
          bodyHtml: draft.bodyHtml,
          bodyText: draft.bodyText,
          isActive: draft.isActive,
          signatureId: draft.signatureId === NO_SIGNATURE ? null : draft.signatureId,
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
        title="Email presets"
        description="Templates and signatures your team sends from. Users cannot compose freely."
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

      <Card className="mb-6">
        {templates.isPending ? (
          <CardContent className="space-y-2 p-5">
            {Array.from({ length: 4 }, (_, index) => (
              <Skeleton key={index} className="h-12 w-full" />
            ))}
          </CardContent>
        ) : rows.length === 0 ? (
          <CardContent className="p-5">
            <EmptyState
              icon={<Mail />}
              title="No presets yet"
              description="Create one so your team has an approved message to send."
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
                  <TableHead>Subject</TableHead>
                  <TableHead>Signature</TableHead>
                  <TableHead>Sent</TableHead>
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
                    <TableCell className="max-w-[240px] truncate text-sm">
                      {template.subject}
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {template.signatureName ?? '—'}
                    </TableCell>
                    <TableCell className="tabular-nums">
                      {formatNumber(template.timesSent)}
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

      <Card>
        <CardHeader className="flex-row items-center justify-between gap-3">
          <div className="space-y-1.5">
            <CardTitle>Signatures</CardTitle>
            <CardDescription>Appended to every send. A preset can name one.</CardDescription>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setSignatureDraft({ name: '', bodyHtml: '', isDefault: false })}
          >
            <Plus />
            New signature
          </Button>
        </CardHeader>
        <CardContent>
          {signatures.isPending ? (
            <Skeleton className="h-20 w-full" />
          ) : (signatures.data?.length ?? 0) === 0 ? (
            <p className="text-sm text-muted-foreground">
              No signatures yet. Sends will go out without a sign-off block.
            </p>
          ) : (
            <ul className="grid gap-2 sm:grid-cols-2">
              {signatures.data?.map((signature) => (
                <li
                  key={signature.id}
                  className="flex items-start justify-between gap-2 rounded-lg border border-border p-3"
                >
                  <div className="min-w-0">
                    <p className="flex items-center gap-1.5 truncate text-sm font-medium">
                      {signature.name}
                      {signature.isDefault && <Badge variant="default">Default</Badge>}
                    </p>
                    <p className="mt-0.5 line-clamp-2 text-xs text-muted-foreground">
                      {signature.bodyHtml.replace(/<[^>]+>/g, ' ').trim()}
                    </p>
                  </div>
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label="Edit signature"
                    onClick={() =>
                      setSignatureDraft({
                        id: signature.id,
                        name: signature.name,
                        bodyHtml: signature.bodyHtml,
                        isDefault: signature.isDefault,
                      })
                    }
                  >
                    <Pencil />
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

      {/* --- preset editor --- */}
      <Dialog open={draft !== null} onOpenChange={(open) => !open && setDraft(null)}>
        <DialogContent className="max-w-2xl">
          <DialogHeader>
            <DialogTitle>{draft?.id ? 'Edit preset' : 'New preset'}</DialogTitle>
            <DialogDescription>
              Placeholders are replaced per recipient when the email is sent.
            </DialogDescription>
          </DialogHeader>

          {draft && (
            <div className="space-y-4">
              <div className="grid gap-3 sm:grid-cols-2">
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
                  <Label htmlFor="tplSignature">Signature</Label>
                  <Select
                    value={draft.signatureId}
                    onValueChange={(next) => setDraft({ ...draft, signatureId: next })}
                  >
                    <SelectTrigger id="tplSignature">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={NO_SIGNATURE}>No signature</SelectItem>
                      {(signatures.data ?? []).map((signature) => (
                        <SelectItem key={signature.id} value={signature.id}>
                          {signature.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
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
                <Label htmlFor="tplSubject">Subject</Label>
                <Input
                  id="tplSubject"
                  value={draft.subject}
                  onChange={(event) => setDraft({ ...draft, subject: event.target.value })}
                  placeholder="Quick question about {{business_name}}"
                />
              </div>

              <div className="space-y-1.5">
                <Label htmlFor="tplBody">Body (HTML)</Label>
                <Textarea
                  id="tplBody"
                  value={draft.bodyHtml}
                  onChange={(event) => setDraft({ ...draft, bodyHtml: event.target.value })}
                  className="min-h-[180px] font-mono text-xs"
                />
                <div className="flex flex-wrap gap-1 pt-1">
                  {TOKENS.map((token) => (
                    <button
                      key={token}
                      type="button"
                      onClick={() => setDraft({ ...draft, bodyHtml: draft.bodyHtml + token })}
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
                    Retired presets stay in the history but cannot be sent.
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
              disabled={!draft?.name.trim() || !draft?.subject.trim() || !draft?.bodyHtml.trim()}
            >
              {draft?.id ? 'Save changes' : 'Create preset'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* --- signature editor --- */}
      <Dialog
        open={signatureDraft !== null}
        onOpenChange={(open) => !open && setSignatureDraft(null)}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{signatureDraft?.id ? 'Edit signature' : 'New signature'}</DialogTitle>
            <DialogDescription>Appended below the message body.</DialogDescription>
          </DialogHeader>

          {signatureDraft && (
            <div className="space-y-4">
              <div className="space-y-1.5">
                <Label htmlFor="sigName">Name</Label>
                <Input
                  id="sigName"
                  value={signatureDraft.name}
                  onChange={(event) =>
                    setSignatureDraft({ ...signatureDraft, name: event.target.value })
                  }
                  placeholder="Company default"
                />
              </div>

              <div className="space-y-1.5">
                <Label htmlFor="sigBody">Content (HTML)</Label>
                <Textarea
                  id="sigBody"
                  value={signatureDraft.bodyHtml}
                  onChange={(event) =>
                    setSignatureDraft({ ...signatureDraft, bodyHtml: event.target.value })
                  }
                  className="min-h-[120px] font-mono text-xs"
                  placeholder="<p><strong>Your Name</strong><br/>Company · you@company.com</p>"
                />
              </div>

              <label className="flex cursor-pointer items-center justify-between gap-4">
                <span className="text-sm">Use as the default signature</span>
                <Switch
                  checked={signatureDraft.isDefault}
                  onCheckedChange={(checked) =>
                    setSignatureDraft({ ...signatureDraft, isDefault: checked })
                  }
                />
              </label>
            </div>
          )}

          <DialogFooter>
            <Button variant="outline" onClick={() => setSignatureDraft(null)}>
              Cancel
            </Button>
            <Button
              loading={saveSignature.isPending}
              disabled={!signatureDraft?.name.trim() || !signatureDraft?.bodyHtml.trim()}
              onClick={() => {
                if (!signatureDraft) return;

                saveSignature.mutate(
                  {
                    id: signatureDraft.id,
                    body: {
                      name: signatureDraft.name,
                      bodyHtml: signatureDraft.bodyHtml,
                      bodyText: signatureDraft.bodyHtml.replace(/<[^>]+>/g, ' ').trim(),
                      isDefault: signatureDraft.isDefault,
                      isActive: true,
                    },
                  },
                  {
                    onSuccess: () => {
                      toast.success('Signature saved.');
                      setSignatureDraft(null);
                    },
                    onError: (error) => toast.error(error.message),
                  },
                );
              }}
            >
              Save
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
              {(confirmDelete?.timesSent ?? 0) > 0
                ? `This preset has been sent ${formatNumber(confirmDelete?.timesSent ?? 0)} time(s), so it will be retired rather than deleted — the email history stays intact.`
                : 'This preset has never been sent, so it will be removed permanently.'}
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
              {(confirmDelete?.timesSent ?? 0) > 0 ? 'Retire preset' : 'Delete preset'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
