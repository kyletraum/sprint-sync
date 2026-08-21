import { useOrganizations } from './useOrganizations';
import { CreateOrganizationForm } from './CreateOrganizationForm';
import { OrganizationList } from './OrganizationList';
import { ActiveOrganizationSwitcher } from './ActiveOrganizationSwitcher';

/**
 * The whole of feature 001 from the user's side: the empty state, the list, the
 * switcher, and creating an organization.
 */
export function OrganizationsPage() {
  const { me, organizations, loading, error, reload } = useOrganizations();

  if (loading) {
    return <p className="muted">Loading…</p>;
  }

  if (error) {
    return (
      <div>
        <p className="error" role="alert">
          {error}
        </p>
        <button type="button" onClick={() => void reload()}>
          Try again
        </button>
      </div>
    );
  }

  const activeOrganizationId = me?.activeOrganizationId ?? null;

  // Empty state: a brand-new user belongs to nothing, and the only thing worth
  // offering them is the way out of it (FR-013, FR-005 edge case).
  if (organizations.length === 0) {
    return <CreateOrganizationForm firstOrganization onCreated={reload} />;
  }

  return (
    <div className="stack">
      <ActiveOrganizationSwitcher
        organizations={organizations}
        activeOrganizationId={activeOrganizationId}
        onSwitched={reload}
      />
      <OrganizationList organizations={organizations} activeOrganizationId={activeOrganizationId} />
      <CreateOrganizationForm onCreated={reload} />
    </div>
  );
}
