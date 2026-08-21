import { AuthenticatedTemplate, UnauthenticatedTemplate, useMsal } from '@azure/msal-react';
import { isAuthConfigured, loginRequest } from './auth/config';
import { OrganizationsPage } from './features/organizations/OrganizationsPage';
import './App.css';

function SignInPanel() {
  const { instance } = useMsal();

  return (
    <div className="panel">
      <h2>Sign in</h2>
      <p className="muted">Sprint Sync uses your organization account to sign you in.</p>
      <button type="button" onClick={() => void instance.loginRedirect(loginRequest)}>
        Sign in
      </button>
    </div>
  );
}

function AccountBar() {
  const { instance, accounts } = useMsal();
  const account = accounts[0];

  return (
    <div className="account-bar">
      <span className="muted">{account?.username ?? account?.name}</span>
      <button type="button" onClick={() => void instance.logoutRedirect()}>
        Sign out
      </button>
    </div>
  );
}

export default function App() {
  return (
    <main className="app">
      <header>
        <h1>Sprint Sync</h1>
      </header>

      {!isAuthConfigured ? (
        // Say what is wrong here rather than failing at the first redirect with
        // an opaque MSAL error.
        <div className="panel">
          <h2>Sign-in is not configured</h2>
          <p className="muted">
            Copy <code>.env.example</code> to <code>.env.local</code> and set the Entra External ID
            values, then restart the dev server.
          </p>
        </div>
      ) : (
        <>
          <UnauthenticatedTemplate>
            <SignInPanel />
          </UnauthenticatedTemplate>

          <AuthenticatedTemplate>
            <AccountBar />
            <OrganizationsPage />
          </AuthenticatedTemplate>
        </>
      )}
    </main>
  );
}
