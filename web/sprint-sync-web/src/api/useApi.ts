import { useMemo } from 'react';
import { SprintSyncApi } from './client';
import { useAccessToken } from '../auth/useAccessToken';

/** The API client bound to the signed-in user's token. */
export function useApi(): SprintSyncApi {
  const getToken = useAccessToken();
  return useMemo(() => new SprintSyncApi(getToken), [getToken]);
}
