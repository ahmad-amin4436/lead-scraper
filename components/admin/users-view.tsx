'use client';

import * as React from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { ChevronLeft, ChevronRight, MoreHorizontal, Plus, Search, UserPlus } from 'lucide-react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';

import { PageHeader } from '@/components/shared/page-header';
import { useAuth } from '@/components/providers/auth-provider';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Alert, AlertDescription, EmptyState, Skeleton } from '@/components/ui/misc';
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
  useAssignUserRoles,
  useCreateUser,
  useDeleteUser,
  useResetUserPassword,
  useRoles,
  useUpdateUser,
  useUsers,
  type CreateUserInput,
  type UserListQuery,
} from '@/hooks/use-admin';
import { useDebouncedValue } from '@/hooks/use-debounced-value';
import { ApiClientError } from '@/lib/api-client';
import type { BackendRole, BackendUser } from '@/lib/backend/types';
import { createUserSchema, type CreateUserInput as CreateUserFormInput } from '@/lib/validation/admin.schema';
import { formatDateTime } from '@/utils/format';

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof ApiClientError ? error.message : fallback;
}

function RolesEditor({
  roles,
  selected,
  onToggle,
  disabled,
}: {
  roles: BackendRole[];
  selected: string[];
  onToggle: (name: string) => void;
  disabled?: boolean;
}) {
  if (roles.length === 0) {
    return <p className="text-sm text-muted-foreground">No roles available.</p>;
  }

  return (
    <div className="grid grid-cols-1 gap-1.5">
      {roles.map((role) => (
        <label key={role.id} className="flex items-center gap-2 text-sm">
          <Checkbox
            checked={selected.includes(role.name)}
            onCheckedChange={() => onToggle(role.name)}
            disabled={disabled}
          />
          <span className="font-medium">{role.name}</span>
          {role.description && (
            <span className="truncate text-xs text-muted-foreground">— {role.description}</span>
          )}
        </label>
      ))}
    </div>
  );
}

interface AddUserDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  roles: BackendRole[];
  onSubmit: (input: CreateUserInput) => Promise<void>;
}

function AddUserDialog({ open, onOpenChange, roles, onSubmit }: AddUserDialogProps) {
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<CreateUserFormInput>({
    resolver: zodResolver(createUserSchema),
    defaultValues: { firstName: '', lastName: '', email: '', password: '' },
  });

  const [active, setActive] = React.useState(true);
  const [selectedRoles, setSelectedRoles] = React.useState<string[]>([]);
  const [formError, setFormError] = React.useState<string | null>(null);

  function toggleRole(name: string) {
    setSelectedRoles((current) =>
      current.includes(name) ? current.filter((role) => role !== name) : [...current, name],
    );
  }

  async function onFormSubmit(values: CreateUserFormInput) {
    setFormError(null);
    try {
      await onSubmit({ ...values, isActive: active, roles: selectedRoles });
      onOpenChange(false);
      toast.success('User created');
    } catch (error) {
      setFormError(errorMessage(error, 'Could not create the user.'));
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={handleSubmit(onFormSubmit)} noValidate>
          <DialogHeader>
            <DialogTitle>Add user</DialogTitle>
            <DialogDescription>Create an account with an initial password and role.</DialogDescription>
          </DialogHeader>

          <div className="space-y-4">
            {formError && (
              <Alert variant="destructive">
                <AlertDescription>{formError}</AlertDescription>
              </Alert>
            )}

            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-2">
                <Label htmlFor="firstName">First name</Label>
                <Input id="firstName" aria-invalid={errors.firstName ? true : undefined} {...register('firstName')} />
                {errors.firstName && <p className="text-xs text-destructive">{errors.firstName.message}</p>}
              </div>
              <div className="space-y-2">
                <Label htmlFor="lastName">Last name</Label>
                <Input id="lastName" aria-invalid={errors.lastName ? true : undefined} {...register('lastName')} />
                {errors.lastName && <p className="text-xs text-destructive">{errors.lastName.message}</p>}
              </div>
            </div>

            <div className="space-y-2">
              <Label htmlFor="email">Email</Label>
              <Input id="email" type="email" autoComplete="off" aria-invalid={errors.email ? true : undefined} {...register('email')} />
              {errors.email && <p className="text-xs text-destructive">{errors.email.message}</p>}
            </div>

            <div className="space-y-2">
              <Label htmlFor="password">Password</Label>
              <Input id="password" type="password" autoComplete="new-password" aria-invalid={errors.password ? true : undefined} {...register('password')} />
              {errors.password && <p className="text-xs text-destructive">{errors.password.message}</p>}
            </div>

            <div className="flex items-center justify-between">
              <Label htmlFor="isActive">Active</Label>
              <Switch id="isActive" checked={active} onCheckedChange={setActive} />
            </div>

            <div className="space-y-2">
              <Label>Roles</Label>
              <RolesEditor roles={roles} selected={selectedRoles} onToggle={toggleRole} />
            </div>
          </div>

          <DialogFooter className="mt-6">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" loading={isSubmitting}>
              Create user
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/** What the caller submitted, split by which endpoint each part belongs to. */
interface EditUserPatch {
  profile?: { firstName: string; lastName: string; isActive: boolean };
  roles?: string[];
}

interface EditUserDialogProps {
  user: BackendUser;
  roles: BackendRole[];
  canEditProfile: boolean;
  canManageRoles: boolean;
  onSubmit: (patch: EditUserPatch) => Promise<void>;
  onClose: () => void;
}

function EditUserDialog({
  user,
  roles,
  canEditProfile,
  canManageRoles,
  onSubmit,
  onClose,
}: EditUserDialogProps) {
  const [firstName, setFirstName] = React.useState(user.firstName);
  const [lastName, setLastName] = React.useState(user.lastName);
  const [active, setActive] = React.useState(user.isActive);
  const [selectedRoles, setSelectedRoles] = React.useState<string[]>(user.roles);
  const [submitting, setSubmitting] = React.useState(false);
  const [formError, setFormError] = React.useState<string | null>(null);

  function toggleRole(name: string) {
    setSelectedRoles((current) =>
      current.includes(name) ? current.filter((role) => role !== name) : [...current, name],
    );
  }

  async function onSave() {
    setFormError(null);
    setSubmitting(true);
    try {
      // Role assignment is a distinct endpoint from the profile update (and a
      // distinct permission) on the backend — only submit each part the
      // caller is actually allowed to change.
      await onSubmit({
        profile: canEditProfile ? { firstName, lastName, isActive: active } : undefined,
        roles: canManageRoles ? selectedRoles : undefined,
      });
      onClose();
      toast.success('User updated');
    } catch (error) {
      setFormError(errorMessage(error, 'Could not update the user.'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Edit {user.fullName}</DialogTitle>
          <DialogDescription>Update the profile, activation state and role assignments.</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          {formError && (
            <Alert variant="destructive">
              <AlertDescription>{formError}</AlertDescription>
            </Alert>
          )}

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-2">
              <Label htmlFor="edit-firstName">First name</Label>
              <Input
                id="edit-firstName"
                value={firstName}
                onChange={(event) => setFirstName(event.target.value)}
                disabled={!canEditProfile}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="edit-lastName">Last name</Label>
              <Input
                id="edit-lastName"
                value={lastName}
                onChange={(event) => setLastName(event.target.value)}
                disabled={!canEditProfile}
              />
            </div>
          </div>

          <div className="flex items-center justify-between">
            <Label htmlFor="edit-active">Active</Label>
            <Switch id="edit-active" checked={active} onCheckedChange={setActive} disabled={!canEditProfile} />
          </div>

          <div className="space-y-2">
            <Label>Roles</Label>
            <RolesEditor
              roles={roles}
              selected={selectedRoles}
              onToggle={toggleRole}
              disabled={!canManageRoles}
            />
            {!canManageRoles && (
              <p className="text-xs text-muted-foreground">
                You don&apos;t have permission to change role assignments.
              </p>
            )}
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

export function UsersView() {
  const { hasPermission } = useAuth();
  const [search, setSearch] = React.useState('');
  const [page, setPage] = React.useState(1);
  const [pageSize] = React.useState(25);
  const [addOpen, setAddOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<BackendUser | null>(null);
  const [resetting, setResetting] = React.useState<BackendUser | null>(null);
  const [newPassword, setNewPassword] = React.useState('');
  const [deleting, setDeleting] = React.useState<BackendUser | null>(null);

  const debouncedSearch = useDebouncedValue(search, 350);

  const query = React.useMemo<UserListQuery>(
    () => ({
      search: debouncedSearch || undefined,
      page,
      pageSize,
    }),
    [debouncedSearch, page, pageSize],
  );

  const users = useUsers(query);
  const roles = useRoles();
  const createUser = useCreateUser();
  const updateUser = useUpdateUser();
  const assignRoles = useAssignUserRoles();
  const deleteUser = useDeleteUser();
  const resetPassword = useResetUserPassword();

  const canEditProfile = hasPermission('users.update');
  const canManageRoles = hasPermission('users.manage-roles');

  const canManage =
    canEditProfile ||
    canManageRoles ||
    hasPermission('users.create') ||
    hasPermission('users.reset-password') ||
    hasPermission('users.delete');

  const rows = users.data?.items ?? [];
  const total = users.data?.total ?? 0;
  const pageCount = users.data?.pageCount ?? 1;

  function updateSearch(value: string) {
    setSearch(value);
    setPage(1);
  }

  async function handleResetPassword(user: BackendUser) {
    if (newPassword.length < 8) {
      toast.error('Password must be at least 8 characters.');
      return;
    }
    try {
      await resetPassword.mutateAsync({ id: user.id, newPassword });
      setResetting(null);
      setNewPassword('');
      toast.success(`Password reset for ${user.fullName}`);
    } catch (error) {
      toast.error(errorMessage(error, 'Could not reset the password.'));
    }
  }

  async function handleDelete(user: BackendUser) {
    try {
      await deleteUser.mutateAsync(user.id);
      setDeleting(null);
      toast.success('User deleted');
    } catch (error) {
      toast.error(errorMessage(error, 'Could not delete the user.'));
    }
  }

  return (
    <>
      <PageHeader
        title="Users"
        description="Accounts, roles and access within the backend."
        actions={
          hasPermission('users.create') ? (
            <Button onClick={() => setAddOpen(true)}>
              <Plus />
              Add user
            </Button>
          ) : undefined
        }
      />

      <Card className="p-3">
        <div className="mb-3 flex flex-wrap items-center gap-3">
          <div className="relative min-w-52 flex-1">
            <Search className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              value={search}
              onChange={(event) => updateSearch(event.target.value)}
              placeholder="Search by name or email…"
              className="pl-9"
            />
          </div>
        </div>

        <TableContainer>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>User</TableHead>
                <TableHead>Roles</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Last login</TableHead>
                <TableHead className="w-14 text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {users.isPending ? (
                <TableRow>
                  <TableCell colSpan={5} className="py-10">
                    <div className="space-y-2">
                      <Skeleton className="h-4 w-3/4" />
                      <Skeleton className="h-4 w-1/2" />
                      <Skeleton className="h-4 w-2/3" />
                    </div>
                  </TableCell>
                </TableRow>
              ) : rows.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={5}>
                    <EmptyState
                      icon={<UserPlus />}
                      title="No users found"
                      description="Try a different search, or add the first account."
                      action={
                        hasPermission('users.create') ? (
                          <Button onClick={() => setAddOpen(true)}>
                            <Plus />
                            Add user
                          </Button>
                        ) : undefined
                      }
                      className="my-2 border-0"
                    />
                  </TableCell>
                </TableRow>
              ) : (
                rows.map((user) => (
                  <TableRow key={user.id}>
                    <TableCell>
                      <p className="font-medium">{user.fullName}</p>
                      <p className="text-xs text-muted-foreground">{user.email}</p>
                    </TableCell>
                    <TableCell>
                      <div className="flex flex-wrap gap-1">
                        {user.roles.length === 0 ? (
                          <Badge variant="muted">No roles</Badge>
                        ) : (
                          user.roles.map((role) => (
                            <Badge key={role} variant="secondary">
                              {role}
                            </Badge>
                          ))
                        )}
                      </div>
                    </TableCell>
                    <TableCell>
                      {user.isActive ? (
                        <Badge variant="success">Active</Badge>
                      ) : (
                        <Badge variant="muted">Inactive</Badge>
                      )}
                    </TableCell>
                    <TableCell className="text-muted-foreground">
                      {user.lastLoginAt ? formatDateTime(user.lastLoginAt) : 'Never'}
                    </TableCell>
                    <TableCell className="text-right">
                      {canManage && (
                        <DropdownMenu>
                          <DropdownMenuTrigger asChild>
                            <Button variant="ghost" size="icon-sm" aria-label="User actions">
                              <MoreHorizontal />
                            </Button>
                          </DropdownMenuTrigger>
                          <DropdownMenuContent align="end">
                            {hasPermission('users.update') && (
                              <DropdownMenuItem onSelect={() => setEditing(user)}>Edit</DropdownMenuItem>
                            )}
                            {hasPermission('users.reset-password') && (
                              <DropdownMenuItem onSelect={() => { setResetting(user); setNewPassword(''); }}>
                                Reset password
                              </DropdownMenuItem>
                            )}
                            {(hasPermission('users.update') || hasPermission('users.manage-roles')) && (
                              <DropdownMenuItem onSelect={() => setEditing(user)}>Roles</DropdownMenuItem>
                            )}
                            <DropdownMenuSeparator />
                            {hasPermission('users.delete') && (
                              <DropdownMenuItem destructive onSelect={() => setDeleting(user)}>
                                Delete
                              </DropdownMenuItem>
                            )}
                          </DropdownMenuContent>
                        </DropdownMenu>
                      )}
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </TableContainer>

        <div className="mt-3 flex flex-wrap items-center justify-between gap-3 px-1">
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <span>{total} total</span>
          </div>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={page <= 1 || users.isPending}
              onClick={() => setPage((current) => Math.max(1, current - 1))}
            >
              <ChevronLeft />
              Previous
            </Button>
            <span className="text-sm text-muted-foreground">
              Page {page} of {pageCount}
            </span>
            <Button
              variant="outline"
              size="sm"
              disabled={page >= pageCount || users.isPending}
              onClick={() => setPage((current) => current + 1)}
            >
              Next
              <ChevronRight />
            </Button>
          </div>
        </div>
      </Card>

      {addOpen && (
        <AddUserDialog
          open={addOpen}
          onOpenChange={setAddOpen}
          roles={roles.data ?? []}
          onSubmit={async (input) => {
            await createUser.mutateAsync(input);
          }}
        />
      )}

      {editing && (
        <EditUserDialog
          user={editing}
          roles={roles.data ?? []}
          canEditProfile={canEditProfile}
          canManageRoles={canManageRoles}
          onClose={() => setEditing(null)}
          onSubmit={async (patch) => {
            // Two independent endpoints, each gated by its own permission —
            // only call the ones the dialog actually populated.
            if (patch.profile) {
              await updateUser.mutateAsync({ id: editing.id, patch: patch.profile });
            }
            if (patch.roles) {
              await assignRoles.mutateAsync({ id: editing.id, roles: patch.roles });
            }
          }}
        />
      )}

      <Dialog open={!!resetting} onOpenChange={(open) => !open && setResetting(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Reset password</DialogTitle>
            <DialogDescription>
              Set a new password for {resetting?.fullName}. Their existing sessions will be revoked.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-2">
            <Label htmlFor="new-password">New password</Label>
            <Input
              id="new-password"
              type="password"
              autoComplete="new-password"
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
            />
            <p className="text-xs text-muted-foreground">At least 8 characters.</p>
          </div>
          <DialogFooter className="mt-6">
            <Button type="button" variant="outline" onClick={() => setResetting(null)}>
              Cancel
            </Button>
            <Button
              type="button"
              onClick={() => resetting && handleResetPassword(resetting)}
              loading={resetPassword.isPending}
            >
              Reset password
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={!!deleting} onOpenChange={(open) => !open && setDeleting(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete user</DialogTitle>
            <DialogDescription>
              This permanently deletes {deleting?.fullName} and cannot be undone. Prefer deactivating
              instead if they might return.
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
              loading={deleteUser.isPending}
            >
              Delete user
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
