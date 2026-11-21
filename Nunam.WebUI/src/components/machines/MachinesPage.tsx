import { useCallback, useEffect, useState } from 'react';
import { ArrowLeft, Pause, Play, RotateCcw, RotateCw, Share2 } from 'lucide-react';
import {
  getIndexer,
  getIndexers,
  sendIndexerCommand,
  type IndexerCommandKind,
  type IndexerDetails,
  type IndexerListItem,
  type MachineSummary,
} from '@/api/nunam';
import { MachineAccessDialog } from '@/components/MachineAccessDialog';
import { MachineActivity } from '@/components/machines/MachineActivity';
import { MachineLogs } from '@/components/machines/MachineLogs';
import { MachineOverview } from '@/components/machines/MachineOverview';
import { MachineSettings } from '@/components/machines/MachineSettings';
import { commandLabels, presence, presenceOf } from '@/components/machines/format';
import { Button } from '@/components/ui/button';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';

const LIST_REFRESH_MS = 30_000;
const DETAIL_REFRESH_MS = 10_000;

function Dot({ className }: { className: string }) {
  return <span className={`inline-block size-2 shrink-0 rounded-full ${className}`} aria-hidden />;
}

function MachineList({ machines, selected, onSelect }: {
  machines: IndexerListItem[];
  selected: string | null;
  onSelect: (id: string) => void;
}) {
  return (
    <nav aria-label="Machines">
      <ul className="space-y-0.5">
        {machines.map((m) => {
          const p = presenceOf(m);
          return (
            <li key={m.machineId}>
              <button
                type="button"
                onClick={() => onSelect(m.machineId)}
                aria-current={m.machineId === selected ? 'page' : undefined}
                className="flex w-full items-start gap-2.5 rounded-md px-2.5 py-2 text-left text-sm hover:bg-muted aria-[current=page]:bg-muted"
              >
                <Dot className={`mt-1.5 ${p.dot}`} />
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-medium">{m.name}</span>
                  <span className="block truncate text-xs text-muted-foreground">
                    {p.label} · {m.fileCount.toLocaleString()} files
                    {m.queued > 0 && ` · ${m.queued.toLocaleString()} queued`}
                    {m.failed > 0 && <span className="text-destructive"> · {m.failed.toLocaleString()} failed</span>}
                  </span>
                </span>
              </button>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}

function Actions({ details, onSent }: { details: IndexerDetails; onSent: () => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const status = details.status;
  const remote = status?.remoteManagement ?? false;
  const failed = details.jobs.filter((j) => j.kind === 'parse' && j.status === 'failed').reduce((n, j) => n + j.count, 0);
  const off = remote ? undefined : "This machine doesn't allow remote management";

  const send = async (kind: IndexerCommandKind) => {
    setBusy(true);
    setError(null);
    try {
      await sendIndexerCommand(details.machineId, kind);
      onSent();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-wrap items-center gap-2">
      {status?.state === 'paused' ? (
        <Button variant="outline" size="sm" disabled={!remote || busy} title={off} onClick={() => send('resume')}><Play />{commandLabels.resume}</Button>
      ) : (
        <Button variant="outline" size="sm" disabled={!remote || busy} title={off ?? 'Stop parsing; changes are still queued'} onClick={() => send('pause')}><Pause />{commandLabels.pause}</Button>
      )}
      <Button variant="outline" size="sm" disabled={!remote || busy} title={off ?? 'Restart the workers and scan every watch path again'} onClick={() => send('restart')}>
        <RotateCw />{commandLabels.restart}
      </Button>
      <Button variant="outline" size="sm" disabled={!remote || busy || failed === 0} title={off} onClick={() => send('retryFailed')}>
        <RotateCcw />{commandLabels.retryFailed}{failed > 0 && ` (${failed.toLocaleString()})`}
      </Button>
      {error && <p role="alert" className="w-full text-sm text-destructive">{error}</p>}
      {remote && status && !status.online && (
        <p className="w-full text-xs text-muted-foreground">The indexer is offline; actions wait until it is back.</p>
      )}
    </div>
  );
}

function MachineDetail({ machineId, onChanged }: { machineId: string; onChanged: () => void }) {
  const [details, setDetails] = useState<IndexerDetails | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState('overview');
  const [sharing, setSharing] = useState(false);

  const load = useCallback((signal?: AbortSignal) => {
    getIndexer(machineId, signal).then(
      (d) => { setDetails(d); setError(null); },
      (e: Error) => { if (e.name !== 'AbortError') setError(e.message); },
    );
  }, [machineId]);

  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal);
    const timer = setInterval(() => load(controller.signal), DETAIL_REFRESH_MS);
    return () => { controller.abort(); clearInterval(timer); };
  }, [load]);

  // A command takes the indexer up to a few seconds to pick up; check back sooner than the regular refresh.
  const refreshSoon = () => {
    load();
    onChanged();
    for (const ms of [3000, 7000]) setTimeout(() => { load(); onChanged(); }, ms);
  };

  if (error && !details) return <p role="alert" className="text-sm text-destructive">{error}</p>;
  if (!details) return <p className="text-sm text-muted-foreground">Loading…</p>;

  const status = details.status;
  const p = presence(status?.online ?? false, status?.state ?? null, status !== null);

  return (
    <div className="min-w-0 space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="truncate text-lg font-semibold tracking-tight">{details.name}</h2>
          <p className="flex items-center gap-1.5 text-sm text-muted-foreground">
            <Dot className={p.dot} />{p.label}
            <span>· {details.hostname}</span>
            {status && <span>· remote management {status.remoteManagement ? 'on' : 'off'}</span>}
          </p>
        </div>
        {details.canManage && (
          <Button variant="ghost" size="sm" onClick={() => setSharing(true)}><Share2 />Share</Button>
        )}
      </div>

      {details.canManage && status && <Actions details={details} onSent={refreshSoon} />}

      {details.canManage ? (
        <Tabs value={tab} onValueChange={setTab}>
          <TabsList>
            <TabsTrigger value="overview">Overview</TabsTrigger>
            <TabsTrigger value="settings">Settings</TabsTrigger>
            <TabsTrigger value="logs">Logs</TabsTrigger>
            <TabsTrigger value="activity">Activity</TabsTrigger>
          </TabsList>
          <TabsContent value="overview"><MachineOverview details={details} /></TabsContent>
          <TabsContent value="settings"><MachineSettings key={machineId} details={details} onSaved={refreshSoon} /></TabsContent>
          <TabsContent value="logs"><MachineLogs machineId={machineId} /></TabsContent>
          <TabsContent value="activity"><MachineActivity commands={details.commands ?? []} /></TabsContent>
        </Tabs>
      ) : (
        <MachineOverview details={details} />
      )}

      <MachineAccessDialog
        machine={sharing ? ({ id: details.machineId, name: details.name } as MachineSummary) : null}
        onOpenChange={(open) => !open && setSharing(false)}
      />
    </div>
  );
}

/** Every indexer the user can see: status for all, settings, logs and actions for admins and owners. */
export function MachinesPage({ machineId, onNavigate }: { machineId: string | null; onNavigate: (path: string) => void }) {
  const [machines, setMachines] = useState<IndexerListItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadList = useCallback((signal?: AbortSignal) => {
    getIndexers(signal).then(setMachines, (e: Error) => { if (e.name !== 'AbortError') setError(e.message); });
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    loadList(controller.signal);
    const timer = setInterval(() => loadList(controller.signal), LIST_REFRESH_MS);
    return () => { controller.abort(); clearInterval(timer); };
  }, [loadList]);

  const selected = machineId ?? machines?.[0]?.machineId ?? null;

  return (
    <div className="mx-auto max-w-6xl px-4 py-4">
      <div className="mb-4 flex items-center gap-3">
        <Button variant="ghost" size="sm" onClick={() => onNavigate('/')}><ArrowLeft />Search</Button>
        <h1 className="text-base font-semibold">Machines</h1>
      </div>

      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
      {machines?.length === 0 && (
        <p className="text-sm text-muted-foreground">No machines are shared with you yet. Ask an admin or a machine's owner.</p>
      )}

      {machines && machines.length > 0 && (
        <div className="grid gap-6 md:grid-cols-[15rem_1fr]">
          <MachineList machines={machines} selected={selected} onSelect={(id) => onNavigate(`/machines/${id}`)} />
          {selected && <MachineDetail key={selected} machineId={selected} onChanged={() => loadList()} />}
        </div>
      )}
    </div>
  );
}
