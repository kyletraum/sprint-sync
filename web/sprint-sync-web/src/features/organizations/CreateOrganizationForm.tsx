import { useState, type FormEvent } from 'react';
import { useApi } from '../../api/useApi';
import { ApiError } from '../../api/client';

interface Props {
  onCreated: () => void | Promise<void>;
  /** Copy shifts when this is the user's very first organization. */
  firstOrganization?: boolean;
}

const MaxNameLength = 100;

/**
 * US1 / FR-001/012: create an organization and become its Owner.
 *
 * The field is trimmed and length-checked here for a fast answer, but the
 * server validates independently — this is a courtesy, not the enforcement.
 */
export function CreateOrganizationForm({ onCreated, firstOrganization = false }: Props) {
  const api = useApi();
  const [name, setName] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const trimmed = name.trim();
  const invalid = trimmed.length === 0 || trimmed.length > MaxNameLength;

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (invalid || submitting) {
      return;
    }

    setSubmitting(true);
    setError(null);
    try {
      await api.createOrganization({ name: trimmed });
      setName('');
      await onCreated();
    } catch (caught) {
      setError(
        caught instanceof ApiError
          ? (Object.values(caught.fieldErrors).flat()[0] ?? caught.message)
          : 'Could not create the organization.',
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form className="create-org" onSubmit={handleSubmit} aria-labelledby="create-org-heading">
      <h2 id="create-org-heading">
        {firstOrganization ? 'Create your first organization' : 'Create an organization'}
      </h2>
      {firstOrganization && (
        <p className="muted">
          You do not belong to any organization yet. Creating one makes you its Owner.
        </p>
      )}

      <label htmlFor="org-name">Organization name</label>
      <input
        id="org-name"
        name="name"
        value={name}
        maxLength={MaxNameLength}
        autoComplete="off"
        placeholder="Acme"
        disabled={submitting}
        onChange={(event) => setName(event.target.value)}
      />

      <button type="submit" disabled={invalid || submitting}>
        {submitting ? 'Creating…' : 'Create organization'}
      </button>

      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
    </form>
  );
}
