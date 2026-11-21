import { useEffect, useState } from 'react';
import { useLocation } from 'react-router';

export const useActiveSection = (ids: string[]): string | null => {
  const { pathname } = useLocation();
  const [active, setActive] = useState<string | null>(null);

  useEffect(() => {
    const elements = ids
      .map((id) => document.getElementById(id))
      .filter((element): element is HTMLElement => element !== null);

    if (elements.length === 0) return;

    const visible = new Set<string>();

    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            visible.add(entry.target.id);
          } else {
            visible.delete(entry.target.id);
          }
        }

        const current = ids.find((id) => visible.has(id));
        if (current) setActive(current);
      },
      { rootMargin: '-45% 0px -45% 0px' },
    );

    elements.forEach((element) => observer.observe(element));

    return () => observer.disconnect();
  }, [ids, pathname]);

  return pathname === '/' ? active : null;
};
