import { useEffect, useState } from 'react';
import { Check, Copy } from 'lucide-react';
import { createEnrollment, getEnrollment, type EnrollmentCreated, type PackageKind } from '@/api/nunam';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';

const POLL_MS = 3000;

type Platform = 'windows' | 'linux';

/** Single quotes keep both shells from expanding anything in the value. */
const quote = (value: string) => `'${value.replace(/'/g, "'\\''")}'`;
const psQuote = (value: string) => `'${value.replace(/'/g, "''")}'`;

function installCommand(platform: Platform, server: string, code: string, name: string, remote: boolean) {
  if (platform === 'windows') {
    return `& ([scriptblock]::Create((irm ${psQuote(`${server}/api/agent/install.ps1`)}))) -Server ${psQuote(server)} -Code ${psQuote(code)}`
      + (name ? ` -Name ${psQuote(name)}` : '')
      + (remote ? ' -RemoteManagement' : '');
  }
  return `curl -fsSL ${quote(`${server}/api/agent/install.sh`)} | sudo sh -s -- --server ${quote(server)} --code ${quote(code)}`
    + (name ? ` --name ${quote(name)}` : '')
    + (remote ? ' --remote-management' : '');
}

/** For a machine that already has the indexer installed, or when the server has no package to hand out. */
function enrollCommand(platform: Platform, server: string, code: string, name: string, remote: boolean) {
  const q = platform === 'windows' ? psQuote : quote;
  const args = ['enroll', '--server', q(server), '--code', q(code), ...(name ? ['--name', q(name)] : []), ...(remote ? ['--remote-management'] : [])].join(' ');
  return platform === 'windows'
    ? `& "$env:ProgramFiles\\Nunam\\Indexer\\Nunam.Indexer.exe" ${args}`
    : `sudo /usr/lib/nunam-indexer/Nunam.Indexer ${args}`;
}

function CommandBox({ command, label }: { command: string; label: string }) {
  const [copied, setCopied] = useState(false);
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(command);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard needs a secure context (https or localhost); over plain LAN http it can be blocked.
      window.prompt(label, command);
    }
  };
  return (
    <div className="flex items-start gap-2 rounded-md border bg-muted/50 p-2">
      <code className="min-w-0 flex-1 font-mono text-xs break-all select-all">{command}</code>
      <Button variant="ghost" size="icon-sm" onClick={copy} aria-label={label} title="Copy">
        {copied ? <Check /> : <Copy />}
      </Button>
    </div>
  );
}

function PlatformSteps({ platform, enrollment, server, name, remote }: {
  platform: Platform;
  enrollment: EnrollmentCreated;
  server: string;
  name: string;
  remote: boolean;
}) {
  const needed: PackageKind[] = platform === 'windows' ? ['msi'] : ['deb', 'rpm'];
  const available = needed.filter((k) => enrollment.packages.includes(k));
  const install = installCommand(platform, server, enrollment.code, name, remote);
  const enroll = enrollCommand(platform, server, enrollment.code, name, remote);

  return (
    <div className="space-y-3 text-sm">
      {available.length > 0 ? (
        <>
          <p>
            {platform === 'windows'
              ? 'On the machine, open PowerShell as administrator and run:'
              : 'On the machine, run this in a terminal (Debian, Ubuntu or Fedora):'}
          </p>
          <CommandBox command={install} label="Copy the install command" />
          <p className="text-xs text-muted-foreground">
            {platform === 'windows'
              ? 'It installs the indexer, connects it to this server and asks which account the service runs as.'
              : 'It installs the indexer, connects it to this server and asks which user the service runs as.'}
            {platform === 'linux' && available.length < needed.length && ` Only the ${available[0]} package is on the server.`}
          </p>
        </>
      ) : (
        <p className="text-muted-foreground">
          The server has no {needed.join(' or ')} package to hand out. Install the indexer from its package first
          (see Adding a machine in the docs), or copy one into the server's package folder for a one-line install.
        </p>
      )}
      <details className="text-xs">
        <summary className="cursor-pointer text-muted-foreground">Indexer already installed?</summary>
        <div className="mt-2 space-y-2">
          <p className="text-muted-foreground">
            {platform === 'windows'
              ? 'Connect it with this instead, in PowerShell as administrator, then run Configure Nunam Indexer from the Start menu:'
              : 'Connect it with this instead, then run sudo systemctl restart nunam-indexer:'}
          </p>
          <CommandBox command={enroll} label="Copy the connect command" />
        </div>
      </details>
    </div>
  );
}

/**
 * Creates a one-time code and shows the command that installs the indexer on a machine and connects
 * it here, then follows the code until the machine connects.
 */
export function AddMachineDialog({ open, onOpenChange, machine, onConnected }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Reconnect this machine (new token, same files) instead of adding one. */
  machine?: { id: string; name: string };
  onConnected: (machineId: string) => void;
}) {
  const [name, setName] = useState('');
  const [remote, setRemote] = useState(true);
  const [platform, setPlatform] = useState<Platform>(() => (navigator.userAgent.includes('Windows') ? 'windows' : 'linux'));
  const [enrollment, setEnrollment] = useState<EnrollmentCreated | null>(null);
  const [connectedId, setConnectedId] = useState<string | null>(null);
  const [expired, setExpired] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const server = window.location.origin;

  // A closed dialog starts over: the code is single-use and shown once.
  const handleOpenChange = (next: boolean) => {
    if (!next) {
      setEnrollment(null);
      setConnectedId(null);
      setExpired(false);
      setError(null);
      setName('');
    }
    onOpenChange(next);
  };

  useEffect(() => {
    if (!enrollment || connectedId) return;
    const controller = new AbortController();
    const timer = setInterval(() => {
      if (Date.parse(enrollment.expiresAt) < Date.now()) {
        setExpired(true);
        clearInterval(timer);
        return;
      }
      getEnrollment(enrollment.id, controller.signal).then(
        (state) => { if (state.machineId) { setConnectedId(state.machineId); onConnected(state.machineId); } },
        () => {},
      );
    }, POLL_MS);
    return () => { controller.abort(); clearInterval(timer); };
  }, [enrollment, connectedId, onConnected]);

  const create = async () => {
    setBusy(true);
    setError(null);
    setExpired(false);
    try {
      setEnrollment(await createEnrollment(machine ? { machineId: machine.id } : { name: name.trim() || undefined }));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const expiresAt = enrollment && new Date(enrollment.expiresAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{machine ? `Reconnect ${machine.name}` : 'Add a machine'}</DialogTitle>
          <DialogDescription>
            {machine
              ? 'Gives the machine a new token and keeps its files. Its old token stops working once the command has run.'
              : 'Installs the indexer on another computer and connects it to this server.'}
          </DialogDescription>
        </DialogHeader>

        {!enrollment && (
          <div className="space-y-4">
            {!machine && (
              <div className="space-y-1.5">
                <Label htmlFor="machine-name">Name</Label>
                <Input id="machine-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="The computer's hostname" maxLength={200} />
              </div>
            )}
            <label className="flex items-start gap-2.5 text-sm">
              <input type="checkbox" className="mt-0.5 size-4 accent-foreground" checked={remote} onChange={(e) => setRemote(e.target.checked)} />
              <span>
                Manage it from here
                <span className="block text-xs text-muted-foreground">
                  Watch paths and other settings can then be changed on the Machines page, and the indexer paused or restarted.
                  The command turns it on in the machine's own settings.
                </span>
              </span>
            </label>
            {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
            <div className="flex justify-end">
              <Button onClick={create} disabled={busy}>Get the command</Button>
            </div>
          </div>
        )}

        {enrollment && !connectedId && (
          <div className="min-w-0 space-y-4">
            <Tabs value={platform} onValueChange={(v) => setPlatform(v as Platform)}>
              <TabsList>
                <TabsTrigger value="windows">Windows</TabsTrigger>
                <TabsTrigger value="linux">Linux</TabsTrigger>
              </TabsList>
              {(['windows', 'linux'] as const).map((p) => (
                <TabsContent key={p} value={p} className="pt-2">
                  <PlatformSteps platform={p} enrollment={enrollment} server={server} name={machine ? '' : name.trim()} remote={remote} />
                </TabsContent>
              ))}
            </Tabs>
            {window.location.protocol === 'http:' && (
              <p className="text-xs text-muted-foreground">
                This page is on plain http, so the machine's token travels unencrypted on your network. Put Nunam behind
                https to avoid that; the command uses the address you opened this page with.
              </p>
            )}
            {expired ? (
              <div className="flex items-center justify-between gap-3 text-sm">
                <p className="text-destructive">The code has expired.</p>
                <Button variant="outline" size="sm" onClick={create} disabled={busy}>New code</Button>
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">Waiting for the machine to connect… The code works once, until {expiresAt}.</p>
            )}
          </div>
        )}

        {connectedId && (
          <div className="space-y-4 text-sm">
            <p>
              The machine is connected.
              {!machine && (remote
                ? ' Add its watch paths under Settings; it indexes nothing until it has some.'
                : ' Set its watch paths in appsettings.Local.json on the machine.')}
            </p>
            <div className="flex justify-end">
              <Button onClick={() => handleOpenChange(false)}>Done</Button>
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
