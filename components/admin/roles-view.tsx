'use client';

import * as React from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { KeyRound, Plus, ShieldCheck, Users as UsersIcon } from 'lucide-react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';

import { PageHeader } from '@/components/shared/page-header';
import { useAuth } from '@/components/providers/auth-provider';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Alert, AlertDescription, EmptyState, Skeleton } from '@/components/ui/misc';
import { Textarea } from '@/components/ui/input';
import {
  useCreateRole,
  useDeleteRole,
  usePermissions,
  useRoles,
  useSetRolePermissions,
  useUpdateRole,
  type CreateRoleInput,
} from '@/hooks/use-admin';
import { ApiClientError } from '@/lib/api-client';
import type { BackendPermission, BackendRole } from '@/lib/backend/types';
import { createRoleSchema, type CreateRoleInput as CreateRoleFormInput } from '@/lib/validation/admin.schema';

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof ApiClientError ? error.message : fallback;
}

interface CreateRoleDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSubmit: (input: CreateRoleInput) => Promise<void>;
}

function CreateRoleDialog({ open, onOpenChange, onSubmit }: CreateRoleDialogProps) {
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<CreateRoleFormInput>({
    resolver: zodResolver(createRoleSchema),
    defaultValues: { name: '', description: '' },
  });

  const [formError, setFormError] = React.useState<string | null>(null);

  async function onFormSubmit(values: CreateRoleFormInput) {
    setFormError(null);
    try {
      await onSubmit({ ...values, permissions: [] });
      onOpenChange(false);
      toast.success('Role created');
    } catch (error) {
      setFormError(errorMessage(error, 'Could not create the role.'));
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={handleSubmit(onFormSubmit)} noValidate>
          <DialogHeader>
            <DialogTitle>Create role</DialogTitle>
            <DialogDescription>Name the role now; permissions can be added next.</DialogDescription>
          </DialogHeader>

          <div className="space-y-4">
            {formError && (
              <Alert variant="destructive">
                <AlertDescription>{formError}</AlertDescription>
              </Alert>
            )}

            <div className="space-y-2">
              <Label htmlFor="role-name">Name</Label>
              <Input id="role-name" aria-invalid={errors.name ? true : undefined} {...register('name')} />
              {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
            </div>

            <div className="space-y-2">
              <Label htmlFor="role-description">Description</Label>
              <Textarea id="role-description" {...register('description')} />
              {errors.description && (
                <p className="text-xs text-destructive">{errors.description.message}</p>
              )}
            </div>
          </div>

          <DialogFooter className="mt-6">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" loading={isSubmitting}>
              Create role
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

interface EditRoleDialogProps {
  role: BackendRole;
  onSubmit: (patch: { name?: string; description?: string }) => Promise<void>;
  onClose: () => void;
}

function EditRoleDialog({ role, onSubmit, onClose }: EditRoleDialogProps) {
  const [name, setName] = React.useState(role.name);
  const [description, setDescription] = React.useState(role.description);
  const [submitting, setSubmitting] = React.useState(false);
  const [formError, setFormError] = React.useState<string | null>(null);

  async function onSave() {
    setFormError(null);
    setSubmitting(true);
    try {
      await onSubmit({ name: name.trim() || undefined, description });
      onClose();
      toast.success('Role updated');
    } catch (error) {
      setFormError(errorMessage(error, 'Could not update the role.'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Edit {role.name}</DialogTitle>
          <DialogDescription>Rename or re-describe the role.</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          {formError && (
            <Alert variant="destructive">
              <AlertDescription>{formError}</AlertDescription>
            </Alert>
          )}

          <div className="space-y-2">
            <Label htmlFor="edit-role-name">Name</Label>
            <Input id="edit-role-name" value={name} onChange={(event) => setName(event.target.value)} />
          </div>

          <div className="space-y-2">
            <Label htmlFor="edit-role-description">Description</Label>
            <Textarea
              id="edit-role-description"
              value={description}
              onChange={(event) => setDescription(event.target.value)}
            />
          </div>
        </div>

        <DialogFooter className="mt-6">
          <Button type="button" variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button type="button" onClick={onSave} loading={submitting}>
            Save changes
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

interface PermissionsDialogProps {
  role: BackendRole;
  allPermissions: BackendPermission[];
  onSubmit: (permissions: string[]) => Promise<void>;
  onClose: () => void;
}

function PermissionsDialog({ role, allPermissions, onSubmit, onClose }: PermissionsDialogProps) {
  const [selected, setSelected] = React.useState<string[]>(role.permissions);
  const [submitting, setSubmitting] = React.useState(false);
  const [formError, setFormError] = React.useState<string | null>(null);

  const grouped = React.useMemo(() => {
    const map = new Map<string, BackendPermission[]>();
    for (const permission of allPermissions) {
      const list = map.get(permission.group) ?? [];
      list.push(permission);
      map.set(permission.group, list);
    }
    return Array.from(map.entries());
  }, [allPermissions]);

  function toggle(name: string) {
    setSelected((current) =>
      current.includes(name) ? current.filter((permission) => permission !== name) : [...current, name],
    );
  }

  async function onSave() {
    setFormError(null);
    setSubmitting(true);
    try {
      await onSubmit(selected);
      onClose();
      toast.success('Permissions updated');
    } catch (error) {
      setFormError(errorMessage(error, 'Could not update the permissions.'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="max-w-xl">
        <DialogHeader>
          <DialogTitle>Permissions for {role.name}</DialogTitle>
          <DialogDescription>
            Members&apos; tokens are invalidated on save, so changes take effect on their next request.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          {formError && (
            <Alert variant="destructive">
              <AlertDescription>{formError}</AlertDescription>
            </Alert>
          )}

          {grouped.length === 0 ? (
            <p className="text-sm text-muted-foreground">Loading permissions…</p>
          ) : (
            grouped.map(([group, permissions]) => (
              <div key={group}>
                <p className="mb-1.5 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                  {group}
                </p>
                <div className="grid grid-cols-1 gap-1.5 sm:grid-cols-2">
                  {permissions.map((permission) => (
                    <label key={permission.name} className="flex items-center gap-2 text-sm">
                      <Checkbox
                        checked={selected.includes(permission.name)}
                        onCheckedChange={() => toggle(permission.name)}
                      />
                      <span className="min-w-0">
                        <span className="block font-medium">{permission.name}</span>
                        <span className="block truncate text-xs text-muted-foreground">
                          {permission.description}
                        </span>
                      </span>
                    </label>
                  ))}
                </div>
              </div>
            ))
          )}
        </div>

        <DialogFooter className="mt-6">
          <Button type="button" variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button type="button" onClick={onSave} loading={submitting}>
            Save permissions
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

export function RolesView() {
  const { hasPermission } = useAuth();
  const roles = useRoles();
  const permissions = usePermissions();
  const createRole = useCreateRole();
  const updateRole = useUpdateRole();
  const deleteRole = useDeleteRole();
  const setRolePermissions = useSetRolePermissions();

  const [createOpen, setCreateOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<BackendRole | null>(null);
  const [permissionRole, setPermissionRole] = React.useState<BackendRole | null>(null);
  const [deleting, setDeleting] = React.useState<BackendRole | null>(null);

  async function handleDelete(role: BackendRole) {
    try {
      await deleteRole.mutateAsync(role.id);
      setDeleting(null);
      toast.success('Role deleted');
    } catch (error) {
      toast.error(errorMessage(error, 'Could not delete the role.'));
    }
  }

  return (
    <>
      <PageHeader
        title="Roles"
        description="What each role can do, and who holds it."
        actions={
          hasPermission('roles.create') ? (
            <Button onClick={() => setCreateOpen(true)}>
              <Plus />
              Create role
            </Button>
          ) : undefined
        }
      />

      {roles.isPending ? (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <Skeleton className="h-40 rounded-xl" />
          <Skeleton className="h-40 rounded-xl" />
          <Skeleton className="h-40 rounded-xl" />
        </div>
      ) : (roles.data ?? []).length === 0 ? (
        <EmptyState
          icon={<ShieldCheck />}
          title="No roles yet"
          description="Create the first role to start granting access."
          action={
            hasPermission('roles.create') ? (
              <Button onClick={() => setCreateOpen(true)}>
                <Plus />
                Create role
              </Button>
            ) : undefined
          }
        />
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {(roles.data ?? []).map((role) => (
            <Card key={role.id}>
              <CardHeader>
                <CardTitle className="flex items-center justify-between gap-2">
                  <span className="truncate">{role.name}</span>
                  {role.isSystemRole && <Badge variant="muted">System</Badge>}
                </CardTitle>
                <CardDescription className="line-clamp-2 min-h-10">
                  {role.description || 'No description.'}
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-3">
                <div className="flex flex-wrap gap-2 text-sm">
                  <Badge variant="secondary" className="gap-1.5">
                    <UsersIcon />
                    {role.userCount} user{role.userCount === 1 ? '' : 's'}
                  </Badge>
                  <Badge variant="secondary" className="gap-1.5">
                    <KeyRound />
                    {role.permissions.length} permission{role.permissions.length === 1 ? '' : 's'}
                  </Badge>
                </div>

                <div className="flex gap-2">
                  {hasPermission('roles.manage-permissions') && (
                    <Button variant="outline" size="sm" className="flex-1" onClick={() => setPermissionRole(role)}>
                      Permissions
                    </Button>
                  )}
                  {hasPermission('roles.update') && (
                    <Button variant="outline" size="sm" onClick={() => setEditing(role)}>
                      Edit
                    </Button>
                  )}
                  {hasPermission('roles.delete') && (
                    <Button variant="outline" size="sm" onClick={() => setDeleting(role)} disabled={role.isSystemRole}>
                      Delete
                    </Button>
                  )}
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      {createOpen && (
        <CreateRoleDialog
          open={createOpen}
          onOpenChange={setCreateOpen}
          onSubmit={async (input) => {
            await createRole.mutateAsync(input);
          }}
        />
      )}

      {editing && (
        <EditRoleDialog
          role={editing}
          onClose={() => setEditing(null)}
          onSubmit={async (patch) => {
            await updateRole.mutateAsync({ id: editing.id, ...patch });
          }}
        />
      )}

      {permissionRole && (
        <PermissionsDialog
          role={permissionRole}
          allPermissions={permissions.data ?? []}
          onClose={() => setPermissionRole(null)}
          onSubmit={async (perms) => {
            await setRolePermissions.mutateAsync({ id: permissionRole.id, permissions: perms });
          }}
        />
      )}

      <Dialog open={!!deleting} onOpenChange={(open) => !open && setDeleting(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete role</DialogTitle>
            <DialogDescription>
              {deleting?.userCount && deleting.userCount > 0
                ? `${deleting.name} is assigned to ${deleting.userCount} user(s). Deleting it removes those grants.`
                : `This permanently deletes the ${deleting?.name} role.`}{' '}
              This cannot be undone.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter className="mt-6">
            <Button type="button" variant="outline" onClick={() => setDeleting(null)}>
              Cancel
            </Button>
            <Button
              type="button"
              variant="destructive"
              onClick={() => deleting && handleDelete(deleting)}
              loading={deleteRole.isPending}
            >
              Delete role
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
