import { useEffect, useRef } from 'react';
import { Search, X } from 'lucide-react';
import type { SearchMode } from '@/api/nunam';
import { Input } from '@/components/ui/input';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';

const modes: { value: SearchMode; label: string; hint: string }[] = [
  { value: 'hybrid', label: 'Hybrid', hint: 'Words and meaning together' },
  { value: 'keyword', label: 'Keywords', hint: 'Exact words; supports "phrases" and -exclusions' },
  { value: 'semantic', label: 'Meaning', hint: 'Natural language, by meaning only' },
];

interface Props {
  query: string;
  mode: SearchMode;
  onQueryChange: (q: string) => void;
  onModeChange: (mode: SearchMode) => void;
}

export function SearchBar({ query, mode, onQueryChange, onModeChange }: Props) {
  const input = useRef<HTMLInputElement>(null);

  // "/" jumps to the search box from anywhere, as on most search-centric sites.
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement;
      if (event.key === '/' && !['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)) {
        event.preventDefault();
        input.current?.focus();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  return (
    <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
      <div className="relative flex-1">
        <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          ref={input}
          type="search"
          value={query}
          onChange={(e) => onQueryChange(e.target.value)}
          placeholder={mode === 'semantic' ? 'Describe what you are looking for…' : 'Search files…'}
          aria-label="Search"
          autoFocus
          className="h-10 pr-9 pl-9 text-base [&::-webkit-search-cancel-button]:hidden"
        />
        {query && (
          <button
            type="button"
            onClick={() => {
              onQueryChange('');
              input.current?.focus();
            }}
            className="absolute top-1/2 right-2.5 -translate-y-1/2 rounded p-0.5 text-muted-foreground hover:text-foreground"
            aria-label="Clear search"
          >
            <X className="size-4" />
          </button>
        )}
      </div>
      <ToggleGroup
        type="single"
        variant="outline"
        value={mode}
        onValueChange={(value) => value && onModeChange(value as SearchMode)}
        aria-label="Search mode"
        className="shrink-0"
      >
        {modes.map((m) => (
          <ToggleGroupItem key={m.value} value={m.value} title={m.hint} className="h-10 px-3">
            {m.label}
          </ToggleGroupItem>
        ))}
      </ToggleGroup>
    </div>
  );
}
