import type { OrganizationSummary } from '../../api/types';

interface Props {
  organizations: OrganizationSummary[];
  activeOrganizationId: string | null;
}

/**
 * US2 / FR-005: the organizations the caller belongs to — exactly what the
 * server returned, in the order it returned them.
 */
export function OrganizationList({ organizations, activeOrganizationId }: Props) {
  return (
    <section aria-labelledby="org-list-heading">
      <h2 id="org-list-heading">Your organizations</h2>

      <ul className="org-list">
        {organizations.map((organization) => {
          const active = organization.id === activeOrganizationId;
          return (
            <li key={organization.id} className={active ? 'org active' : 'org'}>
              <span className="org-name">{organization.name}</span>
              <span className="org-role">{organization.role}</span>
              {active && (
                <span className="badge" aria-label="Active organization">
                  Active
                </span>
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
