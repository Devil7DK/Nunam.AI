import { useCallback, useEffect, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { getIndexerLogs, type IndexerLogEntry, type LogLevel } from '@/api/nunam';
import { Button } from '@/components/ui/button';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';

const PAGE = 200;
const timeFormat = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit' });
const levelStyles: Partial<Record<LogLevel, string>> = {
  warning: 'text-amber-700 dark:text-amber-400',
  error: 'text-destructive',
  critical: 'text-destructive font-semibold',
};
const levelNames: Record<LogLevel, string> = {
  trace: 'trace', debug: 'debug', information: 'info', warning: 'warn', error: 'error', critical: 'critical',
};

export function MachineLogs({ machineId }: { machineId: string }) {
  const [level, setLevel] = useState<LogLevel>('information');
  const [entries, setEntries] = useState<IndexerLogEntry[] | null>(null);
  const [more, setMore] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback((signal?: AbortSignal) => {
    getIndexerLogs(machineId, { level, limit: PAGE }, signal).then(
      (page) => { setEntries(page); setMore(page.length === PAGE); setError(null); },
      (e: Error) => { if (e.name !== 'AbortError') setError(e.message); },
    );
  }, [machineId, level]);

  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const loadOlder = () => {
    if (!entries?.length) return;
    getIndexerLogs(machineId, { level, limit: PAGE, before: entries[entries.length - 1].at }).then(
      (page) => { setEntries([...entries, ...page]); setMore(page.length === PAGE); },
      (e: Error) => setError(e.message),
    );
  };

  return (
    <div className="space-y-3 text-sm">
      <div className="flex flex-wrap items-center gap-2">
        <ToggleGroup type="single" variant="outline" size="sm" value={level} onValueChange={(v) => v && setLevel(v as LogLevel)}>
          <ToggleGroupItem value="information">All</ToggleGroupItem>
          <ToggleGroupItem value="warning">Warnings</ToggleGroupItem>
          <ToggleGroupItem value="error">Errors</ToggleGroupItem>
        </ToggleGroup>
        <Button variant="ghost" size="sm" onClick={() => load()}><RefreshCw />Refresh</Button>
        <span className="text-xs text-muted-foreground">Kept for 7 days. The indexer sends its entries every few seconds.</span>
      </div>

      {error && <p role="alert" className="text-destructive">{error}</p>}
      {entries?.length === 0 && <p className="text-muted-foreground">Nothing logged{level !== 'information' && ' at this level'}.</p>}

      {entries && entries.length > 0 && (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-left font-mono text-xs">
            <tbody>
              {entries.map((e) => (
                <tr key={e.id} className="border-b align-top last:border-b-0">
                  <td className="py-1 pr-3 pl-2 whitespace-nowrap text-muted-foreground tabular-nums">{timeFormat.format(new Date(e.at))}</td>
                  <td className={`py-1 pr-3 whitespace-nowrap ${levelStyles[e.level] ?? 'text-muted-foreground'}`}>{levelNames[e.level]}</td>
                  <td className="py-1 pr-3 whitespace-nowrap text-muted-foreground" title={e.category}>{e.category.split('.').pop()}</td>
                  <td className="py-1 pr-2 break-all whitespace-pre-wrap">
                    {e.message}
                    {e.exception && (
                      <details className="mt-1">
                        <summary className="cursor-pointer text-muted-foreground">Details</summary>
                        <pre className="mt-1 overflow-x-auto text-[11px] whitespace-pre text-muted-foreground">{e.exception}</pre>
                      </details>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {more && <Button variant="outline" size="sm" onClick={loadOlder}>Load older entries</Button>}
    </div>
  );
}
