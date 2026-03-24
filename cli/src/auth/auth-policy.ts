/**
 * Authentication policy helpers used by CLI command entrypoints.
 *
 * Policy:
 * - By default, a bearer token is required to run backend-interacting commands.
 * - Local/dev bypass can be enabled with AGON_ALLOW_ANONYMOUS=true.
 */

export function hasConfiguredAuthToken(storedToken: string | null): boolean {
  return Boolean(
    process.env.AGON_AUTH_TOKEN?.trim()
    || process.env.AGON_BEARER_TOKEN?.trim()
    || storedToken
  );
}

export function allowsAnonymousBypass(): boolean {
  const raw = process.env.AGON_ALLOW_ANONYMOUS?.trim().toLowerCase();
  return raw === '1' || raw === 'true' || raw === 'yes' || raw === 'on';
}

