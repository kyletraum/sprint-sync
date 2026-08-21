import {
  PublicClientApplication,
  LogLevel,
  type Configuration,
  type PopupRequest,
} from '@azure/msal-browser';

/**
 * MSAL configuration for Entra External ID (CIAM).
 *
 * Every value comes from the environment — nothing about a specific tenant is
 * baked into the source. Copy `.env.example` to `.env.local` and fill it in.
 */
const authority = import.meta.env.VITE_ENTRA_AUTHORITY ?? '';
const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID ?? '';

/** The API scope the access token must carry to reach `/api/v1`. */
export const apiScope = import.meta.env.VITE_API_SCOPE ?? '';

/** Base URL of the Sprint Sync API. */
export const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '/api/v1').replace(/\/$/, '');

/**
 * True when the app has enough configuration to talk to a real identity
 * provider. When false the UI says so plainly rather than failing at the first
 * redirect with an opaque MSAL error.
 */
export const isAuthConfigured = Boolean(authority && clientId);

const configuration: Configuration = {
  auth: {
    clientId,
    authority,
    // CIAM authorities are not under login.microsoftonline.com, so the host
    // must be trusted explicitly.
    knownAuthorities: authority ? [new URL(authority).host] : [],
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
  },
  cache: {
    // Session storage over local: the token dies with the tab rather than
    // lingering on a shared machine.
    cacheLocation: 'sessionStorage',
  },
  system: {
    loggerOptions: {
      logLevel: LogLevel.Warning,
      loggerCallback: (level, message, containsPii) => {
        if (!containsPii && level <= LogLevel.Warning) {
          console.warn('[msal]', message);
        }
      },
    },
  },
};

export const msalInstance = new PublicClientApplication(configuration);

/** Sign-in request: only the API scope, nothing speculative. */
export const loginRequest: PopupRequest = {
  scopes: apiScope ? [apiScope] : [],
};
