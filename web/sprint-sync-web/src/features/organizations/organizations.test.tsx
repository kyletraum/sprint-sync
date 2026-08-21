import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CreateOrganizationForm } from './CreateOrganizationForm';
import { ActiveOrganizationSwitcher } from './ActiveOrganizationSwitcher';
import { ApiError } from '../../api/client';
import type { OrganizationSummary } from '../../api/types';

const createOrganization = vi.fn();
const setActiveOrganization = vi.fn();

vi.mock('../../api/useApi', () => ({
  useApi: () => ({ createOrganization, setActiveOrganization }),
}));

beforeEach(() => {
  vi.clearAllMocks();
});

describe('CreateOrganizationForm', () => {
  it('refuses to submit a blank or whitespace-only name (FR-012)', async () => {
    render(<CreateOrganizationForm onCreated={vi.fn()} />);
    const submit = screen.getByRole('button', { name: /create organization/i });

    expect(submit).toBeDisabled();

    await userEvent.type(screen.getByLabelText(/organization name/i), '   ');
    expect(submit).toBeDisabled();
    expect(createOrganization).not.toHaveBeenCalled();
  });

  it('submits the trimmed name and reloads', async () => {
    const onCreated = vi.fn();
    createOrganization.mockResolvedValue({ id: 'org-1', name: 'Acme', role: 'Owner' });

    render(<CreateOrganizationForm onCreated={onCreated} />);
    await userEvent.type(screen.getByLabelText(/organization name/i), '  Acme  ');
    await userEvent.click(screen.getByRole('button', { name: /create organization/i }));

    await waitFor(() => expect(createOrganization).toHaveBeenCalledWith({ name: 'Acme' }));
    expect(onCreated).toHaveBeenCalled();
  });

  it('shows the empty-state copy for a first organization', () => {
    render(<CreateOrganizationForm firstOrganization onCreated={vi.fn()} />);

    expect(screen.getByRole('heading', { name: /create your first organization/i })).toBeVisible();
  });
});

describe('ActiveOrganizationSwitcher', () => {
  const organizations: OrganizationSummary[] = [
    { id: 'org-1', name: 'Acme', role: 'Owner' },
    { id: 'org-2', name: 'Beta', role: 'Owner' },
  ];

  it('switches to the chosen organization', async () => {
    const onSwitched = vi.fn();
    setActiveOrganization.mockResolvedValue({ userId: 'u', activeOrganizationId: 'org-2' });

    render(
      <ActiveOrganizationSwitcher
        organizations={organizations}
        activeOrganizationId="org-1"
        onSwitched={onSwitched}
      />,
    );
    await userEvent.selectOptions(screen.getByLabelText(/acting in/i), 'org-2');

    await waitFor(() => expect(setActiveOrganization).toHaveBeenCalledWith('org-2'));
    expect(onSwitched).toHaveBeenCalled();
  });

  it('does not reveal whether a refused organization exists (FR-009)', async () => {
    setActiveOrganization.mockRejectedValue(new ApiError(404, { status: 404, title: 'Not Found' }));

    render(
      <ActiveOrganizationSwitcher
        organizations={organizations}
        activeOrganizationId="org-1"
        onSwitched={vi.fn()}
      />,
    );
    await userEvent.selectOptions(screen.getByLabelText(/acting in/i), 'org-2');

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/not available to you/i);
    // The wording must not distinguish "does not exist" from "not yours".
    expect(alert.textContent).not.toMatch(/exist|deleted|removed|not a member/i);
  });
});
