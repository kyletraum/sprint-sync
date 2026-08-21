import { useState } from 'react';
import { useApi } from '../../api/useApi';
import { ApiError } from '../../api/client';
import type { OrganizationSummary } from '../../api/types';

interface Props {
  organizations: OrganizationSummary[];
  activeOrganizationId: string | null;
  onSwitched: () => void | Promise<void>;
}

/**
 * US3 / FR-006: choose which organization you are acting in.
 *
 * The selection is a request, not a decision — the server re-verifies membership
 * and can refuse. On refusal the control snaps back to the server's answer
 * rather than showing a switch that did not happen.
 */
export function ActiveOrganizationSwitcher({
  organizations,
  activeOrganizationId,
  onSwitched,
}: Props) {
  const api = useApi();
  const [switching, setSwitching] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (organizations.length === 0) {
    return null;
  }

  async function handleChange(organizationId: string) {
    if (organizationId === activeOrganizationId) {
      return;
    }

    setSwitching(true);
    setError(null);
    try {
      await api.setActiveOrganization(organizationId);
      await onSwitched();
    } catch (caught) {
      // A 404 here means "not yours" or "gone" — indistinguishable by design,
      // so the message must not speculate about which (FR-009).
      setError(
        caught instanceof ApiError && caught.status === 404
          ? 'That organization is not available to you.'
          : 'Could not switch organization.',
      );
      await onSwitched();
    } finally {
      setSwitching(false);
    }
  }

  return (
    <div className="switcher">
      <label htmlFor="active-org">Acting in</label>
      <select
        id="active-org"
        value={activeOrganizationId ?? ''}
        disabled={switching}
        onChange={(event) => void handleChange(event.target.value)}
      >
        {activeOrganizationId === null && <option value="">Select an organization…</option>}
        {organizations.map((organization) => (
          <option key={organization.id} value={organization.id}>
            {organization.name}
          </option>
        ))}
      </select>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
