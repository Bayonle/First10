import { useEffect, useState } from 'react';

type Theme = 'light' | 'dark';

const stored = (): Theme | null => {
  const v = localStorage.getItem('first10-theme');
  return v === 'light' || v === 'dark' ? v : null;
};

const systemPref = (): Theme =>
  window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';

/** Day shift / night shift. Defaults to OS preference; sticky once chosen. */
export function useTheme() {
  const [theme, setTheme] = useState<Theme>(() => stored() ?? systemPref());

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    localStorage.setItem('first10-theme', theme);
  }, [theme]);

  return { theme, toggle: () => setTheme((t) => (t === 'dark' ? 'light' : 'dark')) };
}
