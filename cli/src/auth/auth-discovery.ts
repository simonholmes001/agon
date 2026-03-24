import type { AuthStatusResponse } from '../api/agon-client.js';
import { normalizeAzureScope } from './azure-cli-token-provider.js';

const tenantAliases = new Set(['common', 'organizations', 'consumers']);

function toTrimmedOrEmpty(value: string | undefined): string {
  return value?.trim() ?? '';
}

export function resolveDiscoveredScope(authStatus: AuthStatusResponse | null): string {
  if (!authStatus) {
    return '';
  }

  const explicitScope = toTrimmedOrEmpty(authStatus.scope);
  if (explicitScope) {
    return explicitScope;
  }

  const audience = toTrimmedOrEmpty(authStatus.audience);
  if (!audience) {
    return '';
  }

  try {
    return normalizeAzureScope(audience);
  } catch {
    return '';
  }
}

export function resolveDiscoveredTenantId(authStatus: AuthStatusResponse | null): string {
  if (!authStatus) {
    return '';
  }

  const explicitTenantId = toTrimmedOrEmpty(authStatus.tenantId);
  if (explicitTenantId) {
    return explicitTenantId;
  }

  const authority = toTrimmedOrEmpty(authStatus.authority);
  if (!authority) {
    return '';
  }

  let parsed: URL;
  try {
    parsed = new URL(authority);
  } catch {
    return '';
  }

  const firstPathSegment = parsed.pathname
    .split('/')
    .map((segment) => segment.trim())
    .filter(Boolean)[0] ?? '';

  if (!firstPathSegment || tenantAliases.has(firstPathSegment.toLowerCase())) {
    return '';
  }

  return firstPathSegment;
}

export function resolveDiscoveredAuthority(authStatus: AuthStatusResponse | null): string {
  if (!authStatus) {
    return '';
  }

  const explicitAuthority = toTrimmedOrEmpty(authStatus.authority);
  if (explicitAuthority) {
    return explicitAuthority;
  }

  const tenantId = resolveDiscoveredTenantId(authStatus);
  if (!tenantId) {
    return '';
  }

  return `https://login.microsoftonline.com/${tenantId}/v2.0`;
}

export function resolveDiscoveredInteractiveClientId(authStatus: AuthStatusResponse | null): string {
  if (!authStatus) {
    return '';
  }

  return toTrimmedOrEmpty(authStatus.interactiveClientId);
}
