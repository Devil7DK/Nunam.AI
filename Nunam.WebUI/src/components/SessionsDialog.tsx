import { useCallback, useEffect, useState } from 'react';
import { endOtherSessions, endSession, getSessions, type SessionInfo } from '@/api/nunam';
import { useAuth } from '@/auth/context';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { formatDateTime, formatRelative } from '@/lib/format';

/** "Firefox on Linux" from a user-agent string; the raw string is in the tooltip. */
function describeAgent(agent: string | null): string {
  if (!agent) return 'Unknown device';
  const browser = /Edg\//.test(agent) ? 'Edge' : /Firefox\//.test(agent) ? 'Firefox' : /Chrome\//.test(agent) ? 'Chrome'
    : /Safari\//.test(agent) ? 'Safari' : /curl\//.test(agent) ? 'curl' : 'Browser';
  const os = /Android/.test(agent) ? 'Android' : /iPhone|iPad/.test(agent) ? 'iOS' : /Windows/.test(agent) ? 'Windows'
    : /Mac OS X/.test(agent) ? 'macOS' : /Linux/.test(agent) ? 'Linux' : null;
  return os ? `${browser} on ${os}` : browser;
}

export function SessionsDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const { signOut } = useAuth();
  const [sessions, setSessions] = useState<SessionInfo[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    getSessions().then(setSessions, (e: Error) => setError(e.message));
  }, []);

  useEffect(() => {
    if (open) load();
  }, [open, load]);

  const run = (action: () => Promise<void>) => {
    setError(null);
    action().then(load, (e: Error) => setError(e.message));
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Signed-in devices</DialogTitle>
          <DialogDescription>Sign a device out if you no longer use it or don't recognise it.</DialogDescription>
        </DialogHeader>

        <ul className="divide-y text-sm">
          {sessions?.map((s) => (
            <li key={s.id} className="flex items-center gap-3 py-2">
              <div className="min-w-0 flex-1">
                <div className="truncate font-medium" title={s.userAgent ?? undefined}>
                  {describeAgent(s.userAgent)}
                  {s.current && <span className="ml-2 text-xs font-normal text-muted-foreground">this device</span>}
                </div>
                <div className="text-xs text-muted-foreground">
                  {s.ipAddress ?? 'unknown address'} · signed in {formatDateTime(s.created)} · active {formatRelative(s.lastSeen)}
                </div>
              </div>
              {!s.current && (
                <Button variant="outline" size="sm" onClick={() => run(() => endSession(s.id))}>Sign out</Button>
              )}
            </li>
          ))}
        </ul>

        {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" size="sm" disabled={!sessions || sessions.length < 2} onClick={() => run(endOtherSessions)}>
            Sign out other devices
          </Button>
          <Button variant="ghost" size="sm" onClick={() => void signOut(true)}>Sign out everywhere</Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
