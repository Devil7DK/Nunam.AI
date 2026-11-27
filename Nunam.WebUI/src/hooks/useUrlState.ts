import { useCallback, useState } from 'react';
import type { FileType, SearchMode } from '@/api/nunam';

export interface Filters {
  q: string;
  mode: SearchMode;
  type: FileType | '';
  machine: string;
  ext: string;
  from: string;
  to: string;
  /** AI description: any, with or without. */
  described: '' | 'with' | 'without';
}

const defaults: Filters = { q: '', mode: 'hybrid', type: '', machine: '', ext: '', from: '', to: '', described: '' };
const modes: SearchMode[] = ['hybrid', 'keyword', 'semantic'];
const types: FileType[] = ['document', 'image', 'audio', 'video', 'other'];

function read(): Filters {
  const params = new URLSearchParams(window.location.search);
  const mode = params.get('mode') as SearchMode | null;
  const type = params.get('type') as FileType | null;
  return {
    q: params.get('q') ?? '',
    mode: mode && modes.includes(mode) ? mode : 'hybrid',
    type: type && types.includes(type) ? type : '',
    machine: params.get('machine') ?? '',
    ext: params.get('ext') ?? '',
    from: params.get('from') ?? '',
    to: params.get('to') ?? '',
    described: params.get('described') === 'with' || params.get('described') === 'without'
      ? (params.get('described') as 'with' | 'without')
      : '',
  };
}

/** Search state mirrored into the address bar, so a search can be bookmarked or shared. */
export function useUrlState() {
  const [filters, setFilters] = useState<Filters>(read);

  const update = useCallback((patch: Partial<Filters>) => {
    setFilters((current) => {
      const next = { ...current, ...patch };
      const params = new URLSearchParams();
      for (const [key, value] of Object.entries(next)) {
        if (value !== defaults[key as keyof Filters]) params.set(key, value);
      }
      const query = params.toString();
      window.history.replaceState(null, '', query ? `?${query}` : window.location.pathname);
      return next;
    });
  }, []);

  return [filters, update] as const;
}
