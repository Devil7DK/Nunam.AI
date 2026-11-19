import type { IndexStatus } from '@/api/nunam';
import { formatRelative } from '@/lib/format';

/** One line of index health: how much is indexed, and how far the AI side has caught up. */
export function StatusLine({ status }: { status: IndexStatus | null }) {
  if (!status) return null;

  const pending = status.jobs
    .filter((j) => j.status === 'pending' || j.status === 'processing')
    .reduce((sum, j) => sum + j.count, 0);
  const failed = status.jobs.filter((j) => j.status === 'failed').reduce((sum, j) => sum + j.count, 0);

  return (
    <p className="text-xs text-muted-foreground tabular-nums">
      {status.totalFiles.toLocaleString()} files on {status.machines.length}{' '}
      {status.machines.length === 1 ? 'machine' : 'machines'} · {status.embedded.toLocaleString()} searchable by meaning
      {pending > 0 && ` · ${pending.toLocaleString()} queued`}
      {failed > 0 && <span className="text-destructive"> · {failed.toLocaleString()} failed</span>}
      {' · '}updated {formatRelative(status.lastIndexedAt)}
    </p>
  );
}
