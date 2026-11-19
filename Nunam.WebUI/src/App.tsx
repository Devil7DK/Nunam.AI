import { useEffect, useMemo, useState } from 'react';
import { SlidersHorizontal } from 'lucide-react';
import { getMachines, getStatus, type IndexStatus, type MachineSummary, type SearchResult } from '@/api/nunam';
import { FilterPanel } from '@/components/FilterPanel';
import { PreviewModal } from '@/components/PreviewModal';
import { SearchBar } from '@/components/SearchBar';
import { SearchResults } from '@/components/SearchResults';
import { StatusLine } from '@/components/StatusLine';
import { ThemeToggle } from '@/components/ThemeToggle';
import { Button } from '@/components/ui/button';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { useSearch } from '@/hooks/useSearch';
import { useTheme } from '@/hooks/useTheme';
import { useUrlState } from '@/hooks/useUrlState';

const STATUS_REFRESH_MS = 60_000;

export default function App() {
  const [theme, setTheme] = useTheme();
  const [filters, setFilters] = useUrlState();
  const [machines, setMachines] = useState<MachineSummary[]>([]);
  const [status, setStatus] = useState<IndexStatus | null>(null);
  const [open, setOpen] = useState<SearchResult | null>(null);
  const [showFilters, setShowFilters] = useState(false);

  const query = useDebouncedValue(filters.q.trim(), 300);
  const ext = useDebouncedValue(filters.ext, 500);

  const params = useMemo(
    () => ({
      q: query,
      mode: filters.mode,
      type: filters.type || undefined,
      machine: filters.machine || undefined,
      ext: ext || undefined,
      from: filters.from || undefined,
      to: filters.to || undefined,
    }),
    [query, filters.mode, filters.type, filters.machine, ext, filters.from, filters.to],
  );
  const search = useSearch(params);

  useEffect(() => {
    const controller = new AbortController();
    getMachines(controller.signal).then(setMachines).catch(() => {});

    const refresh = () => getStatus(controller.signal).then(setStatus).catch(() => {});
    refresh();
    const timer = setInterval(refresh, STATUS_REFRESH_MS);
    return () => {
      controller.abort();
      clearInterval(timer);
    };
  }, []);

  const activeFilters = [filters.type, filters.machine, filters.ext, filters.from, filters.to].filter(Boolean).length;

  return (
    <div className="min-h-svh bg-background text-foreground">
      <header className="sticky top-0 z-20 border-b bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/80">
        <div className="mx-auto flex max-w-6xl flex-col gap-2 px-4 py-3">
          <div className="flex items-center gap-3">
            <h1 className="text-lg font-semibold tracking-tight">Nunam</h1>
            <div className="flex-1">
              <StatusLine status={status} />
            </div>
            <ThemeToggle theme={theme} onChange={setTheme} />
          </div>
          <SearchBar
            query={filters.q}
            mode={filters.mode}
            onQueryChange={(q) => setFilters({ q })}
            onModeChange={(mode) => setFilters({ mode })}
          />
        </div>
      </header>

      <div className="mx-auto grid max-w-6xl gap-6 px-4 py-4 lg:grid-cols-[13rem_1fr]">
        <aside className="lg:sticky lg:top-32 lg:self-start">
          <Button
            variant="outline"
            size="sm"
            className="lg:hidden"
            onClick={() => setShowFilters((v) => !v)}
            aria-expanded={showFilters}
          >
            <SlidersHorizontal />
            Filters{activeFilters > 0 && ` (${activeFilters})`}
          </Button>
          <div className={showFilters ? 'mt-3 lg:mt-0' : 'hidden lg:block'}>
            <FilterPanel filters={filters} machines={machines} onChange={setFilters} />
          </div>
        </aside>

        <main className="min-w-0">
          <SearchResults state={search} browsing={query === ''} onOpen={setOpen} />
        </main>
      </div>

      <PreviewModal result={open} onClose={() => setOpen(null)} />
    </div>
  );
}
