import { CircleAlert, CircleCheck } from 'lucide-react';
import type { IndexerDetails, JobCount } from '@/api/nunam';
import { formatDateTime, formatRelative } from '@/lib/format';

const kinds: JobCount['kind'][] = ['parse', 'describe', 'embed'];
const statuses: JobCount['status'][] = ['pending', 'processing', 'done', 'failed'];
const kindLabels = { parse: 'Parse', describe: 'Describe', embed: 'Embed' };

export function Heading({ children }: { children: React.ReactNode }) {
  return <h3 className="mb-2 text-xs font-medium tracking-wide text-muted-foreground uppercase">{children}</h3>;
}

function Fact({ label, children, title }: { label: string; children: React.ReactNode; title?: string }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="truncate" title={title}>{children}</dd>
    </div>
  );
}

function formatRate(perSecond: number): string {
  if (perSecond >= 1) return `${perSecond.toFixed(perSecond >= 10 ? 0 : 1)}/s`;
  const perMinute = perSecond * 60;
  return perMinute >= 1 ? `${perMinute.toFixed(perMinute >= 10 ? 0 : 1)}/min` : `${(perMinute * 60).toFixed(0)}/h`;
}

function formatDuration(seconds: number): string {
  if (seconds < 60) return '< 1 min';
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 48) return `${hours} h ${minutes % 60} min`;
  return `${Math.floor(hours / 24)} d ${hours % 24} h`;
}

function When({ at }: { at: string | null }) {
  return <span title={formatDateTime(at)}>{formatRelative(at)}</span>;
}

function RateCells({ perSecond, remaining }: { perSecond: number; remaining: number }) {
  const muted = 'text-muted-foreground/50';
  return (
    <>
      <td className={`py-1.5 pr-4 text-right ${perSecond > 0 ? '' : muted}`}>{perSecond > 0 ? formatRate(perSecond) : '–'}</td>
      <td className={`py-1.5 text-right ${remaining > 0 && perSecond > 0 ? '' : muted}`}>
        {remaining === 0 ? '–' : perSecond > 0 ? formatDuration(remaining / perSecond) : 'waiting'}
      </td>
    </>
  );
}

export function MachineOverview({ details }: { details: IndexerDetails }) {
  const status = details.status;
  const count = (kind: JobCount['kind'], s: JobCount['status']) =>
    details.jobs.find((j) => j.kind === kind && j.status === s)?.count ?? 0;
  const rate = (kind: JobCount['kind']) => details.rates.find((r) => r.kind === kind)?.perSecond ?? 0;

  return (
    <div className="space-y-6 text-sm">
      <section>
        <dl className="grid grid-cols-2 gap-x-6 gap-y-3 sm:grid-cols-3">
          <Fact label="Files">{details.fileCount.toLocaleString()}</Fact>
          <Fact label="Last indexed"><When at={details.lastIndexedAt} /></Fact>
          <Fact label="Last report">{status ? <When at={status.reportedAt} /> : 'never'}</Fact>
          {status && (
            <>
              <Fact label="Workers started"><When at={status.startedAt} /></Fact>
              <Fact label="Service started"><When at={status.processStartedAt} /></Fact>
              <Fact label="Last scan">
                {status.scanStartedAt === null ? 'not yet'
                  : status.scanFinishedAt === null ? <>running since <When at={status.scanStartedAt} /></>
                  : <>finished <When at={status.scanFinishedAt} /></>}
              </Fact>
              <Fact label="Version">{status.version}</Fact>
              <Fact label="System" title={status.operatingSystem}>{status.operatingSystem}</Fact>
              {status.serviceAccount && <Fact label="Runs as" title={status.serviceAccount}>{status.serviceAccount}</Fact>}
            </>
          )}
        </dl>
        {!status && (
          <p className="mt-3 text-muted-foreground">
            This machine's indexer hasn't reported yet. It does within a minute of starting; if it doesn't, check its log on the machine.
          </p>
        )}
      </section>

      {status && (
        <section>
          <Heading>Watch paths</Heading>
          {status.watchPaths.length === 0 ? (
            <p className="text-muted-foreground">None configured, so nothing is indexed.</p>
          ) : (
            <ul className="space-y-1">
              {status.watchPaths.map((w) => (
                <li key={w.path} className="flex items-start gap-2">
                  {w.available
                    ? <CircleCheck className="mt-0.5 size-4 shrink-0 text-emerald-600" aria-label="Available" />
                    : <CircleAlert className="mt-0.5 size-4 shrink-0 text-destructive" aria-label="Unavailable" />}
                  <div className="min-w-0">
                    <div className="font-mono text-xs break-all">{w.path}</div>
                    {w.problem && <div className="text-xs text-destructive">{w.problem}</div>}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      <section>
        <Heading>Job queue</Heading>
        <div className="overflow-x-auto">
          <table className="w-full text-left tabular-nums">
            <thead className="text-xs text-muted-foreground">
              <tr className="border-b">
                <th className="py-1.5 pr-4 font-medium">Stage</th>
                {statuses.map((s) => <th key={s} className="py-1.5 pr-4 text-right font-medium capitalize">{s}</th>)}
                <th className="py-1.5 pr-4 text-right font-medium" title="Over the last 10 minutes">Speed</th>
                <th className="py-1.5 text-right font-medium" title="At the current speed">Time left</th>
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
                  <RateCells perSecond={rate(kind)} remaining={count(kind, 'pending') + count(kind, 'processing')} />
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}
