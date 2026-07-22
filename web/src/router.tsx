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

const rootRoute = createRootRoute({
  component: () => (
    <div style={{ fontFamily: 'system-ui, sans-serif', padding: '1rem' }}>
      <nav style={{ display: 'flex', gap: '1rem', marginBottom: '1rem' }}>
        <strong>First10</strong>
        <Link to="/console">Console</Link>
        {import.meta.env.DEV && <Link to="/local-chat">Local chat</Link>}
      </nav>
      <Outlet />
    </div>
  ),
});

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
  routeTree: rootRoute.addChildren([indexRoute, consoleRoute, localChatRoute]),
});

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
