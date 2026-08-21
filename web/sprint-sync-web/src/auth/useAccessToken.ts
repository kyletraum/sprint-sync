import { useCallback } from 'react';
import { useMsal } from '@azure/msal-react';
import { InteractionRequiredAuthError } from '@azure/msal-browser';
import { loginRequest } from './config';

/**
 * Returns a function that yields a current access token for the API.
 *
 * Tokens are acquired silently from the MSAL cache and only escalate to an
 * interactive prompt when the library says interaction is genuinely required —
 * so a normal session never bounces the user through a popup.
 */
export function useAccessToken(): () => Promise<string> {
  const { instance, accounts } = useMsal();

  return useCallback(async () => {
    const account = accounts[0];
    if (!account) {
      throw new Error('Not signed in.');
    }

    try {
      const result = await instance.acquireTokenSilent({ ...loginRequest, account });
      return result.accessToken;
    } catch (error) {
      if (error instanceof InteractionRequiredAuthError) {
        const result = await instance.acquireTokenPopup({ ...loginRequest, account });
        return result.accessToken;
      }
      throw error;
    }
  }, [instance, accounts]);
}
