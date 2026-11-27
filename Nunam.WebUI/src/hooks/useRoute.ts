import { useCallback, useEffect, useState } from 'react';

export type Route = { page: 'search' } | { page: 'ai' } | { page: 'machines'; machineId: string | null };

function read(): Route {
  if (/^\/ai\/?$/i.test(window.location.pathname)) return { page: 'ai' };
  const match = window.location.pathname.match(/^\/machines(?:\/([0-9a-f-]{36}))?\/?$/i);
  return match ? { page: 'machines', machineId: match[1] ?? null } : { page: 'search' };
}

/** The page shown, from the path: / for search, /machines[/id] for the machines page, /ai for the AI settings. */
export function useRoute() {
  const [route, setRoute] = useState<Route>(read);

  useEffect(() => {
    const onPop = () => setRoute(read());
    window.addEventListener('popstate', onPop);
    return () => window.removeEventListener('popstate', onPop);
  }, []);

  const navigate = useCallback((path: string) => {
    window.history.pushState(null, '', path);
    setRoute(read());
  }, []);

  return [route, navigate] as const;
}
