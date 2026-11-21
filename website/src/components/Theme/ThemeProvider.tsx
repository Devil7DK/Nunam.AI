import { type FC, type PropsWithChildren, useEffect, useState } from 'react';

import { type Theme, ThemeContext } from './ThemeContext';

const STORAGE_KEY = 'theme';

const getInitialTheme = (): Theme => {
  const current = document.documentElement.dataset.theme;
  if (current === 'light' || current === 'dark') return current;

  const stored = localStorage.getItem(STORAGE_KEY);
  if (stored === 'light' || stored === 'dark') return stored;

  // Dark mode for the win!
  return window.matchMedia('(prefers-color-scheme: light)').matches
    ? 'light'
    : 'dark';
};

export const ThemeProvider: FC<PropsWithChildren> = ({ children }) => {
  const [theme, setTheme] = useState<Theme>(getInitialTheme);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    localStorage.setItem(STORAGE_KEY, theme);
  }, [theme]);

  const toggle = () => setTheme((t) => (t === 'dark' ? 'light' : 'dark'));

  return (
    <ThemeContext.Provider value={{ theme, toggle }}>
      {children}
    </ThemeContext.Provider>
  );
};
