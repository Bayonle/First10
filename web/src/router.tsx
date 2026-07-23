import {
  createRootRoute,
  createRoute,
  createRouter,
  Link,
  Outlet,
  redirect,
} from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import ConsolePage from './pages/ConsolePage';
import LocalChatPage from './pages/LocalChatPage';
import StatsPage from './pages/StatsPage';

function Clock() {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 15_000);
    return () => clearInterval(t);
  }, []);
  return (
    <div className="text-right leading-tight">
      <div className="font-mono text-[1.05rem] font-semibold tracking-wide">
        {now.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}
      </div>
      <div className="text-[0.68rem] text-ink-faint">
        {now.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' })} · WAT
      </div>
    </div>
  );
}

const navLink =
  'rounded-lg px-3 py-1.5 text-[0.78rem] font-semibold uppercase tracking-[0.08em] text-ink-soft ' +
  'hover:text-ink [&.active]:bg-paper-raised [&.active]:text-ink';

function Shell() {
  return (
    <div className="flex h-screen flex-col overflow-hidden">
      <header className="flex shrink-0 items-center gap-4 border-b border-hairline bg-paper px-5 py-2.5">
        <div className="h-8 w-8 rounded-lg bg-sev" aria-hidden />
        <div className="leading-tight">
          <div className="font-display text-[1.02rem] font-extrabold tracking-tight">First10 Dispatch</div>
          <div className="text-[0.66rem] font-semibold uppercase tracking-[0.14em] text-ink-faint">
            FRSC Ogun · Berger–Mowe
          </div>
        </div>
        <span className="ml-2 inline-flex items-center gap-1.5 rounded-full bg-ok-tint px-3 py-1 text-[0.72rem] font-semibold text-ok">
          <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-ok" /> Live feed
        </span>

        <nav className="ml-auto flex items-center gap-1">
          <Link to="/console" className={navLink}>
            Console
          </Link>
          <Link to="/stats" className={navLink}>
            Stats
          </Link>
          {import.meta.env.DEV && (
            <Link to="/local-chat" className={navLink}>
              Local chat
            </Link>
          )}
        </nav>

        <div className="flex items-center gap-3 border-l border-hairline pl-4">
          <Clock />
          <div className="flex items-center gap-2">
            <div className="text-right leading-tight">
              <div className="text-[0.82rem] font-semibold">Duty Officer</div>
              <div className="text-[0.66rem] text-ink-faint">dispatch console</div>
            </div>
            <div className="flex h-9 w-9 items-center justify-center rounded-full bg-act text-[0.78rem] font-bold text-white">
              DO
            </div>
          </div>
        </div>
      </header>
      <main className="min-h-0 flex-1 overflow-y-auto">
        <Outlet />
      </main>
    </div>
  );
}

const rootRoute = createRootRoute({ component: Shell });

const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  beforeLoad: () => {
    throw redirect({ to: '/console' });
  },
});

const consoleRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/console',
  component: ConsolePage,
});

const statsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/stats',
  component: StatsPage,
});

// D-006: dev cockpit is gated client-side too; the API endpoint 404s outside Development.
const localChatRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/local-chat',
  beforeLoad: () => {
    if (!import.meta.env.DEV) throw redirect({ to: '/console' });
  },
  component: LocalChatPage,
});

export const router = createRouter({
  routeTree: rootRoute.addChildren([indexRoute, consoleRoute, statsRoute, localChatRoute]),
});

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
