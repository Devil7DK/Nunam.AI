import type { IndexerCommandInfo } from '@/api/nunam';
import { commandLabels } from '@/components/machines/format';
import { formatDateTime, formatRelative } from '@/lib/format';

const statusStyles: Record<IndexerCommandInfo['status'], string> = {
  pending: 'text-muted-foreground',
  done: 'text-emerald-700 dark:text-emerald-400',
  failed: 'text-destructive',
  rejected: 'text-destructive',
};

export function MachineActivity({ commands }: { commands: IndexerCommandInfo[] }) {
  if (commands.length === 0) return <p className="text-sm text-muted-foreground">No actions sent to this indexer yet.</p>;

  return (
    <ul className="divide-y text-sm">
      {commands.map((c) => (
        <li key={c.id} className="flex flex-wrap items-baseline gap-x-3 gap-y-0.5 py-2">
          <span className="font-medium">{commandLabels[c.kind]}</span>
          <span className={`text-xs capitalize ${statusStyles[c.status]}`}>{c.status === 'pending' ? 'waiting for the indexer' : c.status}</span>
          <span className="text-xs text-muted-foreground" title={formatDateTime(c.requestedAt)}>
            by {c.requestedBy}, {formatRelative(c.requestedAt)}
          </span>
          {c.result && <p className="w-full text-xs text-muted-foreground">{c.result}</p>}
        </li>
      ))}
    </ul>
  );
}
