import { useCallback, useEffect, useState } from 'react';
import { ApiError, search, type SearchParams, type SearchResponse, type SearchResult } from '@/api/nunam';

const PAGE_SIZE = 20;

export interface SearchState {
  results: SearchResult[];
  total: number;
  warning: string | null;
  semanticUsed: boolean;
  loading: boolean;
  loadingMore: boolean;
  error: string | null;
  hasMore: boolean;
  loadMore: () => void;
}

/** The last answered request, tagged with the params it answered. */
interface Settled {
  key: string;
  response: SearchResponse | null;
  results: SearchResult[];
  error: string | null;
}

const message = (e: unknown, fallback: string) => (e instanceof ApiError ? e.message : fallback);

/**
 * Runs a search whenever the params change, aborting the request still in flight so a slow
 * earlier query can't overwrite the results of a later one. While a new search runs, the previous
 * results stay visible.
 */
export function useSearch(params: Omit<SearchParams, 'limit' | 'offset'>): SearchState {
  const key = JSON.stringify(params);
  const [settled, setSettled] = useState<Settled | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    const requested: SearchParams = JSON.parse(key);

    search({ ...requested, limit: PAGE_SIZE, offset: 0 }, controller.signal)
      .then((response) => setSettled({ key, response, results: response.results, error: null }))
      .catch((e: unknown) => {
        if (!controller.signal.aborted) {
          setSettled({ key, response: null, results: [], error: message(e, 'Could not reach the Nunam API.') });
        }
      });

    return () => controller.abort();
  }, [key]);

  const loadMore = useCallback(() => {
    if (!settled || settled.key !== key) return;
    setLoadingMore(true);

    search({ ...JSON.parse(key), limit: PAGE_SIZE, offset: settled.results.length })
      .then((page) =>
        setSettled((current) =>
          current?.key === key ? { ...current, results: [...current.results, ...page.results] } : current,
        ),
      )
      .catch((e: unknown) =>
        setSettled((current) =>
          current?.key === key ? { ...current, error: message(e, 'Could not load more results.') } : current,
        ),
      )
      .finally(() => setLoadingMore(false));
  }, [key, settled]);

  const response = settled?.response ?? null;
  const results = settled?.results ?? [];

  return {
    results,
    total: response?.total ?? 0,
    warning: response?.warning ?? null,
    semanticUsed: response?.semanticUsed ?? false,
    loading: settled?.key !== key,
    loadingMore,
    error: settled?.error ?? null,
    hasMore: response !== null && results.length < response.total,
    loadMore,
  };
}
