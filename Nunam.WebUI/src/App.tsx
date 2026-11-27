import { useEffect, useMemo, useState } from 'react';
import { Server, SlidersHorizontal, Sparkles } from 'lucide-react';
import { getMachines, getStatus, type IndexStatus, type MachineSummary, type SearchResult } from '@/api/nunam';
import { useAuth } from '@/auth/context';
import { AIPage } from '@/components/ai/AIPage';
import { AccountMenu } from '@/components/AccountMenu';
import { FilterPanel } from '@/components/FilterPanel';
import { IndexStatusDialog } from '@/components/IndexStatusDialog';
import { MachinesPage } from '@/components/machines/MachinesPage';
import { PreviewModal } from '@/components/PreviewModal';
import { SearchBar } from '@/components/SearchBar';
import { SearchResults } from '@/components/SearchResults';
import { StatusLine } from '@/components/StatusLine';
import { ThemeToggle } from '@/components/ThemeToggle';
import { Button } from '@/components/ui/button';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { useRoute } from '@/hooks/useRoute';
import { useSearch } from '@/hooks/useSearch';
import { useTheme } from '@/hooks/useTheme';
import { useUrlState } from '@/hooks/useUrlState';

const STATUS_REFRESH_MS = 60_000;

export default function App() {
  const [route, navigate] = useRoute();
  const { user } = useAuth();
  const [theme, setTheme] = useTheme();
  const [filters, setFilters] = useUrlState();
  const [machines, setMachines] = useState<MachineSummary[]>([]);
  const [status, setStatus] = useState<IndexStatus | null>(null);
  const [open, setOpen] = useState<SearchResult | null>(null);
  const [showFilters, setShowFilters] = useState(false);
  const [showStatus, setShowStatus] = useState(false);

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
      described: filters.described ? filters.described === 'with' : undefined,
    }),
    [query, filters.mode, filters.type, filters.machine, ext, filters.from, filters.to, filters.described],
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

  const activeFilters = [filters.type, filters.machine, filters.ext, filters.from, filters.to, filters.described].filter(Boolean).length;
  const headerActions = (
    <>
      <Button variant="ghost" size="sm" onClick={() => navigate('/machines')} aria-current={route.page === 'machines' ? 'page' : undefined}>
        <Server />
        <span className="hidden sm:inline">Machines</span>
      </Button>
      {user.role === 'admin' && (
        <Button variant="ghost" size="sm" onClick={() => navigate('/ai')} aria-current={route.page === 'ai' ? 'page' : undefined}>
          <Sparkles />
          <span className="hidden sm:inline">AI</span>
        </Button>
      )}
      <ThemeToggle theme={theme} onChange={setTheme} />
      <AccountMenu />
    </>
  );

  if (route.page === 'machines' || route.page === 'ai') {
    return (
      <div className="min-h-svh bg-background text-foreground">
        <header className="sticky top-0 z-20 border-b bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/80">
          <div className="mx-auto flex max-w-6xl items-center gap-3 px-4 py-3">
            <button type="button" className="text-lg font-semibold tracking-tight" onClick={() => navigate('/')}>Nunam</button>
            <div className="flex-1" />
            {headerActions}
          </div>
        </header>
        {route.page === 'machines'
          ? <MachinesPage machineId={route.machineId} onNavigate={navigate} />
          : user.role === 'admin'
            ? <AIPage onNavigate={(path) => { window.location.href = path; }} />
            : <p className="mx-auto max-w-4xl px-4 py-6 text-sm text-muted-foreground">Only admins can see the AI settings.</p>}
      </div>
    );
  }

  return (
    <div className="min-h-svh bg-background text-foreground">
      <header className="sticky top-0 z-20 border-b bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/80">
        <div className="mx-auto flex max-w-6xl flex-col gap-2 px-4 py-3">
          <div className="flex items-center gap-3">
            <h1 className="text-lg font-semibold tracking-tight">Nunam</h1>
            <div className="flex-1">
              <StatusLine status={status} onClick={() => setShowStatus(true)} />
            </div>
            {headerActions}
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
          {status?.machines.length === 0 && (
            <p className="mb-4 rounded-md border px-3 py-2 text-sm text-muted-foreground">
              No machines are shared with you yet, so there is nothing to search. Ask an admin or a machine's owner to share one.
            </p>
          )}
          <SearchResults state={search} browsing={query === ''} onOpen={setOpen} />
        </main>
      </div>

      <PreviewModal result={open} onClose={() => setOpen(null)} />
      {status && <IndexStatusDialog status={status} open={showStatus} onOpenChange={setShowStatus} />}
    </div>
  );
}
