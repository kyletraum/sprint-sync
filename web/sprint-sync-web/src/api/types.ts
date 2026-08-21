import type { components } from './schema';

/**
 * Names for the contract types, so features import from here rather than
 * reaching into the generated file. Regenerate with `npm run generate:api`
 * whenever contracts/openapi.yaml changes — never hand-edit `schema.d.ts`.
 */
export type MeResponse = components['schemas']['MeResponse'];
export type OrganizationSummary = components['schemas']['OrganizationSummary'];
export type OrganizationDetail = components['schemas']['OrganizationDetail'];
export type PagedOrganizationSummary = components['schemas']['PagedOrganizationSummary'];
export type CreateOrganizationRequest = components['schemas']['CreateOrganizationRequest'];
export type SetActiveOrganizationRequest = components['schemas']['SetActiveOrganizationRequest'];
export type ProblemDetails = components['schemas']['ProblemDetails'];
export type OrgRole = components['schemas']['OrgRole'];
