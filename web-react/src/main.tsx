import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { App } from './App';
import { createApiClient } from './api/client';
import { AppProvider } from './navigation/AppContext';
import { appForPath } from './navigation/apps';
import { createSession, memberEndpoints, siteAdminEndpoints } from './session/session';
import { SessionProvider } from './session/SessionContext';
import './styles/app.css';

const container = document.getElementById('root');
if (!container) {
  throw new Error('The page has no #root element.');
}

// The page holds two apps that never share a session: the Site admin app under /admin and the member app everywhere
// else. The address the page was opened at says which one this is, and it gets its own API client (the refresh cookie
// it renews from is different) and its own session.
//
// The access token lives only in the client's memory; reloading the page turns the refresh cookie back into a session.
// Until that finishes the status is "unknown" and the routes show a loading line, so a page is never drawn and then
// replaced by the sign-in form.
const app = appForPath(window.location.pathname);
const endpoints = app.kind === 'admin' ? siteAdminEndpoints : memberEndpoints;
const api = createApiClient({ refreshPath: endpoints.refresh });
const session = createSession(api, endpoints);
void session.restore();

createRoot(container).render(
  <StrictMode>
    <AppProvider app={app}>
      <SessionProvider api={api} session={session}>
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </SessionProvider>
    </AppProvider>
  </StrictMode>,
);
