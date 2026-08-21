import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { MsalProvider } from '@azure/msal-react';
import { msalInstance } from './auth/config';
import App from './App.tsx';
import './index.css';

// MSAL must finish initialising (and settle any redirect it is mid-way through)
// before React renders, or the first render sees a half-built account state.
void msalInstance.initialize().then(async () => {
  await msalInstance.handleRedirectPromise();

  const account = msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0];
  if (account) {
    msalInstance.setActiveAccount(account);
  }

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <MsalProvider instance={msalInstance}>
        <App />
      </MsalProvider>
    </StrictMode>,
  );
});
