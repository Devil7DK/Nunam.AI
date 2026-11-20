import { useCallback, useEffect, useState } from 'react';
import { MoreHorizontal } from 'lucide-react';
import {
  ApiError,
  createUser,
  deleteUser,
  generateTemporaryPassword,
  getUsers,
  resetPassword,
  updateUser,
  type UserRole,
  type UserSummary,
} from '@/api/nunam';
import { useAuth } from '@/auth/context';
import { Field } from '@/components/Field';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { fieldMessages } from '@/lib/forms';
import { formatRelative } from '@/lib/format';

interface Handover {
  userName: string;
  password: string;
}

export function UsersDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const { user: me } = useAuth();
  const [users, setUsers] = useState<UserSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [handover, setHandover] = useState<Handover | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null);

  const load = useCallback(() => {
    getUsers().then(setUsers, (e: Error) => setError(e.message));
  }, []);

  useEffect(() => {
    if (open) load();
  }, [open, load]);

  const run = (action: () => Promise<unknown>) => {
    setError(null);
    action().then(load, (e: Error) => setError(e.message));
  };

  const reset = (u: UserSummary) => {
    const password = generateTemporaryPassword();
    run(async () => {
      await resetPassword(u.id, password);
      setHandover({ userName: u.userName, password });
    });
  };

  return (
    <Dialog open={open} onOpenChange={(o) => { onOpenChange(o); if (!o) { setHandover(null); setConfirmDelete(null); } }}>
      <DialogContent className="flex max-h-[90vh] flex-col gap-0 overflow-hidden p-0 sm:max-w-2xl">
        <DialogHeader className="border-b px-5 py-4 text-left">
          <DialogTitle>People</DialogTitle>
          <DialogDescription>
            Members see the machines shared with them; admins see every machine and manage accounts.
          </DialogDescription>
        </DialogHeader>

        <div className="flex-1 space-y-5 overflow-y-auto px-5 py-4 text-sm">
          {handover && (
            <div className="rounded-md border bg-muted/40 p-3" role="status">
              <p>
                Temporary password for <span className="font-medium">{handover.userName}</span>. Give it to them; they
                choose their own when they sign in. It isn't shown again.
              </p>
              <p className="mt-1 font-mono text-base select-all">{handover.password}</p>
            </div>
          )}

          <ul className="divide-y">
            {users?.map((u) => (
              <li key={u.id} className="flex items-center gap-3 py-2">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="truncate font-medium">{u.displayName}</span>
                    {u.role === 'admin' && <Badge variant="secondary">admin</Badge>}
                    {u.disabled && <Badge variant="outline">disabled</Badge>}
                    {u.mustChangePassword && !u.disabled && <Badge variant="outline">temporary password</Badge>}
                  </div>
                  <div className="text-xs text-muted-foreground">
                    {u.userName} · last signed in {formatRelative(u.lastSignIn)}
                  </div>
                </div>

                {confirmDelete === u.id ? (
                  <div className="flex items-center gap-2">
                    <span className="text-xs">Delete {u.userName}?</span>
                    <Button variant="destructive" size="sm" onClick={() => { setConfirmDelete(null); run(() => deleteUser(u.id)); }}>
                      Delete
                    </Button>
                    <Button variant="ghost" size="sm" onClick={() => setConfirmDelete(null)}>Cancel</Button>
                  </div>
                ) : u.id !== me.id && (
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button variant="ghost" size="icon-sm" aria-label={`Actions for ${u.userName}`}><MoreHorizontal /></Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem onSelect={() => run(() => updateUser(u.id, { role: u.role === 'admin' ? 'member' : 'admin' }))}>
                        {u.role === 'admin' ? 'Make member' : 'Make admin'}
                      </DropdownMenuItem>
                      <DropdownMenuItem onSelect={() => reset(u)}>Reset password</DropdownMenuItem>
                      <DropdownMenuItem onSelect={() => run(() => updateUser(u.id, { disabled: !u.disabled }))}>
                        {u.disabled ? 'Enable' : 'Disable'}
                      </DropdownMenuItem>
                      <DropdownMenuSeparator />
                      <DropdownMenuItem variant="destructive" onSelect={() => setConfirmDelete(u.id)}>Delete…</DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                )}
              </li>
            ))}
          </ul>

          {error && <p role="alert" className="text-destructive">{error}</p>}

          <AddPersonForm onAdded={(h) => { setHandover(h); load(); }} />
        </div>
      </DialogContent>
    </Dialog>
  );
}

function AddPersonForm({ onAdded }: { onAdded: (handover: Handover) => void }) {
  const [userName, setUserName] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [role, setRole] = useState<UserRole>('member');
  const [fields, setFields] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    const temporaryPassword = generateTemporaryPassword();
    setBusy(true);
    setFields({});
    setError(null);
    try {
      const created = await createUser({ userName, displayName, role, temporaryPassword });
      onAdded({ userName: created.userName, password: temporaryPassword });
      setUserName('');
      setDisplayName('');
      setRole('member');
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fieldErrors).length > 0) setFields(fieldMessages(e.fieldErrors));
      else setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className="space-y-3 border-t pt-4" onSubmit={submit}>
      <h3 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Add a person</h3>
      <div className="grid gap-3 sm:grid-cols-[1fr_1fr_8rem]">
        <Field label="User name" value={userName} onChange={(e) => setUserName(e.target.value)} error={fields.userName}
          autoComplete="off" autoCapitalize="none" spellCheck={false} required />
        <Field label="Display name" value={displayName} onChange={(e) => setDisplayName(e.target.value)}
          error={fields.displayName} autoComplete="off" />
        <div className="grid gap-1.5">
          <Label htmlFor="new-user-role">Role</Label>
          <Select value={role} onValueChange={(v) => setRole(v as UserRole)}>
            <SelectTrigger id="new-user-role" className="w-full"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="member">Member</SelectItem>
              <SelectItem value="admin">Admin</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </div>
      {(error || fields.temporaryPassword) && <p role="alert" className="text-destructive">{error ?? fields.temporaryPassword}</p>}
      <div className="flex items-center gap-3">
        <Button type="submit" size="sm" disabled={busy}>Add</Button>
        <span className="text-xs text-muted-foreground">A temporary password is generated for you to hand over.</span>
      </div>
    </form>
  );
}
