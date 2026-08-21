import { useCallback, useEffect, useRef, useState } from 'react';
import { useApi } from '../../api/useApi';
import { ApiError } from '../../api/client';
import type { MeResponse, OrganizationSummary } from '../../api/types';

export interface OrganizationsState {
  me: MeResponse | null;
  organizations: OrganizationSummary[];
  loading: boolean;
  error: string | null;
  reload: () => Promise<void>;
}

/**
 * Loads the caller's identity and the organizations they belong to.
 *
 * The list is whatever the server returns — the client never filters it, because
 * membership scoping is the server's job (Principle V). If an organization is not
 * in this response, it does not exist as far as this user is concerned.
 */
export function useOrganizations(): OrganizationsState {
  const api = useApi();
  const [me, setMe] = useState<MeResponse | null>(null);
  const [organizations, setOrganizations] = useState<OrganizationSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Sequence guard: a switch-then-reload can overlap, and a slow earlier
  // response must not overwrite a newer one. Showing a stale organization set
  // after switching would be a tenancy bug in the UI, not just a glitch.
  const requestId = useRef(0);

  const load = useCallback(async () => {
    const current = ++requestId.current;
    setLoading(true);
    setError(null);
    try {
      const [meResponse, page] = await Promise.all([api.getMe(), api.listOrganizations()]);
      if (current !== requestId.current) {
        return;
      }
      setMe(meResponse);
      setOrganizations(page.items);
    } catch (caught) {
      if (current !== requestId.current) {
        return;
      }
      setError(caught instanceof ApiError ? caught.message : 'Could not load your organizations.');
    } finally {
      if (current === requestId.current) {
        setLoading(false);
      }
    }
  }, [api]);

  /** Invalidates whatever load is in flight, so its results are discarded. */
  const abandonInFlight = useCallback(() => {
    requestId.current++;
  }, []);

  useEffect(() => {
    // Async IIFE so no state is set synchronously during the effect.
    void (async () => {
      await load();
    })();

    return abandonInFlight;
  }, [load, abandonInFlight]);

  return { me, organizations, loading, error, reload: load };
}
