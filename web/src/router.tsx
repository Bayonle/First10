import {
  createRootRoute,
  createRoute,
  createRouter,
  Link,
  Outlet,
  redirect,
} from '@tanstack/react-router';
import ConsolePage from './pages/ConsolePage';
import LocalChatPage from './pages/LocalChatPage';
import StatsPage from './pages/StatsPage';
import { useTheme } from './useTheme';

function Shell() {
  const { theme, toggle } = useTheme();
  return (
    <div>
      <header className="flex items-baseline gap-6 border-b-2 border-ink bg-paper-raised px-6 py-3">
        <div className="font-display text-xl font-extrabold uppercase tracking-wide">
          First10
          <small className="font-body ml-3 text-[0.72rem] font-medium tracking-[0.14em] text-ink-soft">
            FRSC OGUN · DISPATCH
          </small>
        </div>
        <nav className="ml-auto flex items-center gap-4">
          <Link
            to="/console"
            className="border-b-2 border-transparent pb-0.5 text-[0.8rem] font-semibold uppercase tracking-[0.08em] text-ink-soft hover:border-ink hover:text-ink [&.active]:border-ink [&.active]:text-ink"
          >
            Console
          </Link>
          <Link
            to="/stats"
            className="border-b-2 border-transparent pb-0.5 text-[0.8rem] font-semibold uppercase tracking-[0.08em] text-ink-soft hover:border-ink hover:text-ink [&.active]:border-ink [&.active]:text-ink"
          >
            Stats
          </Link>
          {import.meta.env.DEV && (
            <Link
              to="/local-chat"
              className="border-b-2 border-transparent pb-0.5 text-[0.8rem] font-semibold uppercase tracking-[0.08em] text-ink-soft hover:border-ink hover:text-ink [&.active]:border-ink [&.active]:text-ink"
            >
              Local chat
            </Link>
          )}
          <button
            onClick={toggle}
            title={theme === 'dark' ? 'Switch to day shift' : 'Switch to night shift'}
            className="ghost-btn"
          >
            {theme === 'dark' ? '☀ day' : '☾ night'}
          </button>
        </nav>
      </header>
      <main className="p-6">
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
