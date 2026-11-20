import { useState } from 'react';
import type { IndexStatus, JobCount, MachineSummary } from '@/api/nunam';
import { CopyPathButton } from '@/components/CopyPathButton';
import { MachineAccessDialog } from '@/components/MachineAccessDialog';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { formatDateTime, formatRelative } from '@/lib/format';

const kinds: JobCount['kind'][] = ['parse', 'describe', 'embed'];
const statuses: JobCount['status'][] = ['pending', 'processing', 'done', 'failed'];
const kindLabels = { parse: 'Parse', describe: 'Describe', embed: 'Embed' };

function Heading({ children }: { children: React.ReactNode }) {
  return <h3 className="mb-2 text-xs font-medium tracking-wide text-muted-foreground uppercase">{children}</h3>;
}

export function IndexStatusDialog({ status, open, onOpenChange }: {
  status: IndexStatus;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const [sharing, setSharing] = useState<MachineSummary | null>(null);
  const canShareAny = status.machines.some((m) => m.canShare);
  const count = (kind: JobCount['kind'], s: JobCount['status']) =>
    status.jobs.find((j) => j.kind === kind && j.status === s)?.count ?? 0;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[90vh] flex-col gap-0 overflow-hidden p-0 sm:max-w-3xl">
        <DialogHeader className="border-b px-5 py-4 text-left">
          <DialogTitle>Index status</DialogTitle>
          <DialogDescription>
            {status.totalFiles.toLocaleString()} files · {status.described.toLocaleString()} described ·{' '}
            {status.embedded.toLocaleString()} searchable by meaning · last indexed {formatRelative(status.lastIndexedAt)}
          </DialogDescription>
        </DialogHeader>

        <div className="flex-1 space-y-6 overflow-y-auto px-5 py-4 text-sm">
          <section>
            <Heading>Machines</Heading>
            <div className="overflow-x-auto">
              <table className="w-full text-left tabular-nums">
                <thead className="text-xs text-muted-foreground">
                  <tr className="border-b">
                    <th className="py-1.5 pr-4 font-medium">Name</th>
                    <th className="py-1.5 pr-4 font-medium">Host</th>
                    <th className="py-1.5 pr-4 text-right font-medium">Files</th>
                    <th className="py-1.5 pr-4 font-medium">Last indexed</th>
                    <th className="py-1.5 pr-4 font-medium">Indexer started</th>
                    {canShareAny && <th className="py-1.5"><span className="sr-only">Sharing</span></th>}
                  </tr>
                </thead>
                <tbody>
                  {status.machines.map((m) => (
                    <tr key={m.id} className="border-b last:border-b-0">
                      <td className="py-1.5 pr-4 font-medium">{m.name}</td>
                      <td className="py-1.5 pr-4 text-muted-foreground">{m.hostname}</td>
                      <td className="py-1.5 pr-4 text-right">{m.fileCount.toLocaleString()}</td>
                      <td className="py-1.5 pr-4" title={formatDateTime(m.lastIndexedAt)}>{formatRelative(m.lastIndexedAt)}</td>
                      <td className="py-1.5 pr-4" title={formatDateTime(m.lastSeen)}>{formatRelative(m.lastSeen)}</td>
                      {canShareAny && (
                        <td className="py-1 text-right">
                          {m.canShare && <Button variant="outline" size="xs" onClick={() => setSharing(m)}>Share</Button>}
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {status.machines.length === 0 && (
              <p className="text-muted-foreground">No machines are shared with you yet. Ask an admin or a machine's owner.</p>
            )}
          </section>

          <section>
            <Heading>Job queue</Heading>
            <div className="overflow-x-auto">
              <table className="w-full text-left tabular-nums">
                <thead className="text-xs text-muted-foreground">
                  <tr className="border-b">
                    <th className="py-1.5 pr-4 font-medium">Stage</th>
                    {statuses.map((s) => (
                      <th key={s} className="py-1.5 pr-4 text-right font-medium capitalize">{s}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {kinds.map((kind) => (
                    <tr key={kind} className="border-b last:border-b-0">
                      <td className="py-1.5 pr-4 font-medium">{kindLabels[kind]}</td>
                      {statuses.map((s) => {
                        const n = count(kind, s);
                        return (
                          <td key={s} className={`py-1.5 pr-4 text-right ${n === 0 ? 'text-muted-foreground/50' : s === 'failed' ? 'text-destructive' : ''}`}>
                            {n.toLocaleString()}
                          </td>
                        );
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <p className="mt-2 text-xs text-muted-foreground">
              Describe and Embed wait in Pending while the AI machine is off. Finished jobs are cleared after a week.
            </p>
          </section>

          <section>
            <Heading>Recent failures</Heading>
            {status.recentFailures.length === 0 ? (
              <p className="text-muted-foreground">None.</p>
            ) : (
              <ul className="divide-y">
                {status.recentFailures.map((f) => (
                  <li key={f.id} className="py-2">
                    <div className="flex items-center gap-2">
                      <span className="font-medium capitalize">{f.kind}</span>
                      <span className="text-xs text-muted-foreground">at {f.stage.toLowerCase()} · {f.machine} · {formatRelative(f.at)}</span>
                    </div>
                    <div className="flex items-center gap-1 font-mono text-xs text-muted-foreground">
                      <span className="truncate" title={f.path}>{f.path}</span>
                      <CopyPathButton path={f.path} />
                    </div>
                    {f.error && <p className="mt-0.5 text-xs text-destructive">{f.error}</p>}
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>
      </DialogContent>
      <MachineAccessDialog machine={sharing} onOpenChange={(o) => !o && setSharing(null)} />
    </Dialog>
  );
}
