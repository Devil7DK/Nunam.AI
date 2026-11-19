import { AlertTriangle, SearchX } from 'lucide-react';
import type { SearchResult } from '@/api/nunam';
import { ResultCard } from '@/components/ResultCard';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import type { SearchState } from '@/hooks/useSearch';

interface Props {
  state: SearchState;
  browsing: boolean;
  onOpen: (result: SearchResult) => void;
}

function LoadingRows() {
  return (
    <ul aria-busy className="divide-y">
      {Array.from({ length: 6 }, (_, i) => (
        <li key={i} className="flex gap-4 px-2 py-3">
          <Skeleton className="size-16 shrink-0" />
          <div className="flex-1 space-y-2 py-1">
            <Skeleton className="h-4 w-1/3" />
            <Skeleton className="h-3 w-1/2" />
            <Skeleton className="h-3 w-5/6" />
          </div>
        </li>
      ))}
    </ul>
  );
}

export function SearchResults({ state, browsing, onOpen }: Props) {
  const { results, total, warning, loading, loadingMore, error, hasMore, loadMore } = state;

  if (loading && results.length === 0) return <LoadingRows />;

  if (error && results.length === 0) {
    return (
      <div className="flex flex-col items-center gap-2 py-16 text-center">
        <AlertTriangle className="size-6 text-destructive" />
        <p className="font-medium">Search failed</p>
        <p className="max-w-md text-sm text-muted-foreground">{error}</p>
      </div>
    );
  }

  if (results.length === 0) {
    return (
      <div className="flex flex-col items-center gap-2 py-16 text-center">
        <SearchX className="size-6 text-muted-foreground" />
        <p className="font-medium">{browsing ? 'No files indexed yet' : 'No matching files'}</p>
        <p className="max-w-md text-sm text-muted-foreground">
          {browsing
            ? 'Files appear here once an indexer has scanned a watched folder.'
            : 'Try fewer words, another search mode, or clearing the filters.'}
        </p>
      </div>
    );
  }

  return (
    <div className={loading ? 'opacity-60 transition-opacity' : undefined}>
      <div className="mb-1 flex items-baseline justify-between px-2 text-xs text-muted-foreground">
        <span className="tabular-nums">
          {browsing ? 'Recently modified' : `${total.toLocaleString()} ${total === 1 ? 'result' : 'results'}`}
        </span>
      </div>

      {warning && (
        <p role="status" className="mb-2 flex items-center gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-sm">
          <AlertTriangle className="size-4 shrink-0 text-amber-600 dark:text-amber-400" />
          {warning}
        </p>
      )}

      <ul>
        {results.map((r) => (
          <ResultCard key={r.id} result={r} onOpen={onOpen} />
        ))}
      </ul>

      {error && <p className="px-2 py-3 text-sm text-destructive">{error}</p>}

      {hasMore && (
        <div className="flex justify-center py-4">
          <Button variant="outline" onClick={loadMore} disabled={loadingMore}>
            {loadingMore ? 'Loading…' : 'Show more'}
          </Button>
        </div>
      )}
    </div>
  );
}
