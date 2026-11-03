import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { App } from './App';
import { createApiClient } from './api/client';
import { createSession } from './session/session';
import { SessionProvider } from './session/SessionContext';
import './styles/app.css';

const container = document.getElementById('root');
if (!container) {
  throw new Error('The page has no #root element.');
}

// One API client and one session for the whole page. The access token lives only in the client's memory; reloading
// the page turns the refresh cookie back into a session. Until that finishes the status is "unknown" and the routes
// show a loading line, so a page is never drawn and then replaced by the sign-in form.
const api = createApiClient({ refreshPath: '/v1/auth/refresh' });
const session = createSession(api);
void session.restore();

createRoot(container).render(
  <StrictMode>
    <SessionProvider api={api} session={session}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </SessionProvider>
  </StrictMode>,
);
