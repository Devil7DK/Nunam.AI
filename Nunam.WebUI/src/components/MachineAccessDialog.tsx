import { useCallback, useEffect, useState } from 'react';
import { X } from 'lucide-react';
import {
  getDirectory,
  getMachineAccess,
  removeMachineAccess,
  setMachineAccess,
  type DirectoryEntry,
  type MachineAccessEntry,
  type MachineRole,
  type MachineSummary,
} from '@/api/nunam';
import { useAuth } from '@/auth/context';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';

const roleLabels: Record<MachineRole, string> = { owner: 'Owner', member: 'Can search' };

function RoleSelect({ value, onChange, disabled, id, label }: {
  value: MachineRole;
  onChange: (role: MachineRole) => void;
  disabled?: boolean;
  id?: string;
  label?: string;
}) {
  return (
    <Select value={value} onValueChange={(v) => onChange(v as MachineRole)} disabled={disabled}>
      <SelectTrigger id={id} size="sm" className="w-32" aria-label={label}><SelectValue /></SelectTrigger>
      <SelectContent>
        <SelectItem value="member">{roleLabels.member}</SelectItem>
        <SelectItem value="owner">{roleLabels.owner}</SelectItem>
      </SelectContent>
    </Select>
  );
}

export function MachineAccessDialog({ machine, onOpenChange }: {
  machine: MachineSummary | null;
  onOpenChange: (open: boolean) => void;
}) {
  const { user: me } = useAuth();
  const [entries, setEntries] = useState<MachineAccessEntry[] | null>(null);
  const [directory, setDirectory] = useState<DirectoryEntry[]>([]);
  const [person, setPerson] = useState('');
  const [role, setRole] = useState<MachineRole>('member');
  const [error, setError] = useState<string | null>(null);

  const machineId = machine?.id;
  const load = useCallback(() => {
    if (!machineId) return;
    getMachineAccess(machineId).then(setEntries, (e: Error) => setError(e.message));
  }, [machineId]);

  useEffect(() => {
    if (!machineId) return;
    load();
    getDirectory().then(setDirectory, () => setDirectory([]));
  }, [machineId, load]);

  const run = (action: () => Promise<void>) => {
    setError(null);
    action().then(load, (e: Error) => setError(e.message));
  };

  const candidates = directory.filter((d) => !entries?.some((e) => e.userId === d.id));
  const isAdmin = me.role === 'admin';

  return (
    <Dialog open={machine !== null} onOpenChange={(o) => { onOpenChange(o); if (!o) { setEntries(null); setPerson(''); setError(null); } }}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Share {machine?.name}</DialogTitle>
          <DialogDescription>
            People here can search this machine's files. Owners can also change who has access.
            {isAdmin && ' Admins see every machine without being listed.'}
          </DialogDescription>
        </DialogHeader>

        <ul className="divide-y text-sm">
          {entries?.length === 0 && <li className="py-2 text-muted-foreground">Not shared with anyone yet.</li>}
          {entries?.map((e) => {
            const self = e.userId === me.id && !isAdmin;
            return (
              <li key={e.userId} className="flex items-center gap-3 py-2">
                <div className="min-w-0 flex-1">
                  <div className="truncate font-medium">{e.displayName}{e.userId === me.id && ' (you)'}</div>
                  <div className="text-xs text-muted-foreground">{e.userName}</div>
                </div>
                <RoleSelect value={e.role} disabled={self} label={`Access for ${e.userName}`}
                  onChange={(r) => run(() => setMachineAccess(machine!.id, e.userId, r))} />
                <Button variant="ghost" size="icon-sm" disabled={self} aria-label={`Remove ${e.userName}`}
                  title={self ? 'Another owner or an admin can change your access' : undefined}
                  onClick={() => run(() => removeMachineAccess(machine!.id, e.userId))}>
                  <X />
                </Button>
              </li>
            );
          })}
        </ul>

        <form
          className="flex flex-wrap items-end gap-2 border-t pt-4"
          onSubmit={(ev) => {
            ev.preventDefault();
            if (person) run(async () => { await setMachineAccess(machine!.id, person, role); setPerson(''); });
          }}
        >
          <div className="grid min-w-40 flex-1 gap-1.5">
            <Label htmlFor="share-person">Add a person</Label>
            <Select value={person} onValueChange={setPerson}>
              <SelectTrigger id="share-person" size="sm" className="w-full">
                <SelectValue placeholder={candidates.length ? 'Choose…' : 'Everyone has access'} />
              </SelectTrigger>
              <SelectContent>
                {candidates.map((d) => (
                  <SelectItem key={d.id} value={d.id}>{d.displayName} <span className="text-muted-foreground">({d.userName})</span></SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <RoleSelect value={role} onChange={setRole} label="Access for the new person" />
          <Button type="submit" size="sm" disabled={!person}>Share</Button>
        </form>

        {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
      </DialogContent>
    </Dialog>
  );
}
