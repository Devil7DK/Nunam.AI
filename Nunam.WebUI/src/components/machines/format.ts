import type { IndexerCommandKind, IndexerListItem, IndexerState } from '@/api/nunam';

export const stateLabels: Record<IndexerState, string> = { running: 'Running', scanning: 'Scanning', paused: 'Paused' };

/** The dot and word for a machine's indexer: off-line wins over what it last reported. */
export function presence(online: boolean, state: IndexerState | null, reported: boolean) {
  if (!reported) return { label: 'Never reported', dot: 'bg-muted-foreground/40' };
  if (!online) return { label: 'Offline', dot: 'bg-muted-foreground/60' };
  if (state === 'paused') return { label: 'Paused', dot: 'bg-amber-500' };
  return { label: stateLabels[state ?? 'running'], dot: 'bg-emerald-500' };
}

export const presenceOf = (m: IndexerListItem) => presence(m.online, m.state, m.reportedAt !== null);

export const commandLabels: Record<IndexerCommandKind, string> = {
  restart: 'Restart and rescan',
  pause: 'Pause',
  resume: 'Resume',
  retryFailed: 'Retry failed files',
};
