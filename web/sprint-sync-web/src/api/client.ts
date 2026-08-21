import { apiBaseUrl } from '../auth/config';
import type {
  CreateOrganizationRequest,
  MeResponse,
  OrganizationDetail,
  OrganizationSummary,
  PagedOrganizationSummary,
  ProblemDetails,
} from './types';

/**
 * An error carrying the API's RFC 9457 ProblemDetails body.
 *
 * Note what is deliberately absent: any way to ask "did that organization
 * exist?". The server answers 404 identically for a non-member and an unknown
 * id, and the client does not try to reconstruct the difference.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails | null;

  constructor(status: number, problem: ProblemDetails | null) {
    super(problem?.detail ?? problem?.title ?? `Request failed with status ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  /** Validation failures carry per-field messages (FR-012). */
  get fieldErrors(): Record<string, string[]> {
    const errors = (this.problem as { errors?: Record<string, string[]> } | null)?.errors;
    return errors ?? {};
  }
}

export type GetToken = () => Promise<string>;

/**
 * The only way this app reaches its data (Principle I): the same public,
 * versioned HTTP contract any third-party client would use. There is no
 * privileged back channel.
 */
export class SprintSyncApi {
  private readonly getToken: GetToken;
  private readonly baseUrl: string;

  constructor(getToken: GetToken, baseUrl: string = apiBaseUrl) {
    this.getToken = getToken;
    this.baseUrl = baseUrl;
  }

  getMe(): Promise<MeResponse> {
    return this.send<MeResponse>('GET', '/me');
  }

  setActiveOrganization(organizationId: string): Promise<MeResponse> {
    return this.send<MeResponse>('PUT', '/me/active-organization', { organizationId });
  }

  listOrganizations(page = 1, pageSize = 50): Promise<PagedOrganizationSummary> {
    return this.send<PagedOrganizationSummary>(
      'GET',
      `/organizations?page=${page}&pageSize=${pageSize}`,
    );
  }

  getOrganization(organizationId: string): Promise<OrganizationDetail> {
    return this.send<OrganizationDetail>('GET', `/organizations/${organizationId}`);
  }

  createOrganization(request: CreateOrganizationRequest): Promise<OrganizationSummary> {
    return this.send<OrganizationSummary>('POST', '/organizations', request);
  }

  private async send<T>(method: string, path: string, body?: unknown): Promise<T> {
    const token = await this.getToken();

    const response = await fetch(`${this.baseUrl}${path}`, {
      method,
      headers: {
        Authorization: `Bearer ${token}`,
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });

    if (!response.ok) {
      throw new ApiError(response.status, await readProblem(response));
    }

    return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
  }
}

async function readProblem(response: Response): Promise<ProblemDetails | null> {
  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    return null;
  }
}
