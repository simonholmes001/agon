export interface EntraDeviceCodePrompt {
  message: string;
  userCode: string;
  verificationUri: string;
  verificationUriComplete?: string;
}

export interface EntraAccessTokenBundle {
  accessToken: string;
  refreshToken?: string;
  expiresAt?: string;
}

export interface EntraDeviceCodeTokenOptions {
  authority: string;
  tenant?: string;
  clientId: string;
  scope: string;
  onUserPrompt?: (prompt: EntraDeviceCodePrompt) => void;
  sleep?: (milliseconds: number) => Promise<void>;
}

type EntraDeviceCodeTokenErrorCode =
  | 'ENTRA_INVALID_AUTHORITY'
  | 'ENTRA_DEVICE_CODE_REQUEST_FAILED'
  | 'ENTRA_DEVICE_AUTH_DECLINED'
  | 'ENTRA_DEVICE_AUTH_EXPIRED'
  | 'ENTRA_DEVICE_TOKEN_FAILED';

interface DeviceCodeResponse {
  device_code: string;
  user_code: string;
  verification_uri: string;
  verification_uri_complete?: string;
  expires_in: number;
  interval?: number;
  message?: string;
}

interface TokenResponse {
  access_token?: string;
  refresh_token?: string;
  expires_in?: number;
  error?: string;
  error_description?: string;
}

const defaultSleep = async (milliseconds: number): Promise<void> =>
  new Promise((resolve) => setTimeout(resolve, milliseconds));

export class EntraDeviceCodeTokenProviderError extends Error {
  readonly code: EntraDeviceCodeTokenErrorCode;
  readonly causeDetail?: string;

  constructor(code: EntraDeviceCodeTokenErrorCode, message: string, causeDetail?: string) {
    super(message);
    this.code = code;
    this.causeDetail = causeDetail;
  }
}

export function normalizeEntraAuthority(authority: string, tenant?: string): string {
  const normalizedAuthority = authority.trim();
  if (!normalizedAuthority) {
    const normalizedTenant = tenant?.trim() ?? '';
    if (!normalizedTenant) {
      throw new EntraDeviceCodeTokenProviderError(
        'ENTRA_INVALID_AUTHORITY',
        'Entra authority is required for device-code sign-in.',
        'Provide --authority or configure backend auth metadata.',
      );
    }
    return `https://login.microsoftonline.com/${normalizedTenant}`;
  }

  let parsed: URL;
  try {
    parsed = new URL(normalizedAuthority);
  } catch {
    throw new EntraDeviceCodeTokenProviderError(
      'ENTRA_INVALID_AUTHORITY',
      'Invalid Entra authority URL.',
      normalizedAuthority,
    );
  }

  const cleanedPath = parsed.pathname
    .replace(/\/+$/, '')
    .replace(/\/oauth2\/v2\.0$/i, '')
    .replace(/\/v2\.0$/i, '');

  parsed.pathname = cleanedPath || '/';
  parsed.search = '';
  parsed.hash = '';
  return parsed.toString().replace(/\/+$/, '');
}

async function postForm(url: string, body: URLSearchParams): Promise<TokenResponse | DeviceCodeResponse> {
  const response = await fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
    },
    body: body.toString(),
  });

  const rawText = await response.text();
  let parsed: TokenResponse | DeviceCodeResponse;
  try {
    parsed = rawText ? JSON.parse(rawText) : {};
  } catch {
    parsed = {};
  }

  if (!response.ok) {
    const error = (parsed as TokenResponse).error;
    const description = (parsed as TokenResponse).error_description;
    throw new EntraDeviceCodeTokenProviderError(
      'ENTRA_DEVICE_TOKEN_FAILED',
      `Entra token endpoint returned ${response.status}.`,
      [error, description].filter(Boolean).join(' | ') || rawText,
    );
  }

  return parsed;
}

function tokenErrorFromCode(errorCode: string, description?: string): EntraDeviceCodeTokenProviderError {
  switch (errorCode) {
    case 'authorization_declined':
      return new EntraDeviceCodeTokenProviderError(
        'ENTRA_DEVICE_AUTH_DECLINED',
        'Sign-in was declined in the browser/device flow.',
        description,
      );
    case 'expired_token':
      return new EntraDeviceCodeTokenProviderError(
        'ENTRA_DEVICE_AUTH_EXPIRED',
        'Device-code sign-in expired before completion.',
        description,
      );
    default:
      return new EntraDeviceCodeTokenProviderError(
        'ENTRA_DEVICE_TOKEN_FAILED',
        'Failed to acquire Entra access token.',
        description || errorCode,
      );
  }
}

export async function acquireTokenFromEntraDeviceCode(options: EntraDeviceCodeTokenOptions): Promise<string> {
  const bundle = await acquireTokenBundleFromEntraDeviceCode(options);
  return bundle.accessToken;
}

export async function acquireTokenBundleFromEntraDeviceCode(
  options: EntraDeviceCodeTokenOptions
): Promise<EntraAccessTokenBundle> {
  const clientId = options.clientId.trim();
  const scope = options.scope.trim();
  if (!clientId) {
    throw new EntraDeviceCodeTokenProviderError(
      'ENTRA_DEVICE_CODE_REQUEST_FAILED',
      'Entra client ID is required for device-code sign-in.',
    );
  }
  if (!scope) {
    throw new EntraDeviceCodeTokenProviderError(
      'ENTRA_DEVICE_CODE_REQUEST_FAILED',
      'Entra scope is required for device-code sign-in.',
    );
  }

  const authorityBase = normalizeEntraAuthority(options.authority, options.tenant);
  const deviceCodeEndpoint = `${authorityBase}/oauth2/v2.0/devicecode`;
  const tokenEndpoint = `${authorityBase}/oauth2/v2.0/token`;
  const sleep = options.sleep ?? defaultSleep;

  let deviceCode: DeviceCodeResponse;
  try {
    deviceCode = await postForm(
      deviceCodeEndpoint,
      new URLSearchParams({
        client_id: clientId,
        scope,
      }),
    ) as DeviceCodeResponse;
  } catch (error) {
    if (error instanceof EntraDeviceCodeTokenProviderError) {
      throw new EntraDeviceCodeTokenProviderError(
        'ENTRA_DEVICE_CODE_REQUEST_FAILED',
        'Failed to start Entra device-code sign-in.',
        error.causeDetail ?? error.message,
      );
    }
    throw error;
  }

  if (!deviceCode.device_code || !deviceCode.verification_uri || !deviceCode.user_code) {
    throw new EntraDeviceCodeTokenProviderError(
      'ENTRA_DEVICE_CODE_REQUEST_FAILED',
      'Entra device-code response was missing required fields.',
    );
  }

  options.onUserPrompt?.({
    message: deviceCode.message ?? `Open ${deviceCode.verification_uri} and enter code ${deviceCode.user_code}.`,
    userCode: deviceCode.user_code,
    verificationUri: deviceCode.verification_uri,
    verificationUriComplete: deviceCode.verification_uri_complete,
  });

  const maxWaitMs = Math.max(deviceCode.expires_in, 60) * 1000;
  const startedAt = Date.now();
  let intervalMs = Math.max(deviceCode.interval ?? 5, 1) * 1000;

  while (Date.now() - startedAt < maxWaitMs) {
    await sleep(intervalMs);

    let tokenResponse: TokenResponse;
    try {
      tokenResponse = await postForm(
        tokenEndpoint,
        new URLSearchParams({
          grant_type: 'urn:ietf:params:oauth:grant-type:device_code',
          client_id: clientId,
          device_code: deviceCode.device_code,
        }),
      ) as TokenResponse;
    } catch (error) {
      if (error instanceof EntraDeviceCodeTokenProviderError) {
        const detail = error.causeDetail?.toLowerCase() ?? '';
        if (detail.includes('authorization_pending')) {
          continue;
        }
        if (detail.includes('slow_down')) {
          intervalMs += 5000;
          continue;
        }
        if (detail.includes('authorization_declined')) {
          throw tokenErrorFromCode('authorization_declined', error.causeDetail);
        }
        if (detail.includes('expired_token')) {
          throw tokenErrorFromCode('expired_token', error.causeDetail);
        }
        throw new EntraDeviceCodeTokenProviderError(
          'ENTRA_DEVICE_TOKEN_FAILED',
          'Failed while polling Entra token endpoint.',
          error.causeDetail ?? error.message,
        );
      }
      throw error;
    }

    if (tokenResponse.access_token) {
      return {
        accessToken: tokenResponse.access_token,
        refreshToken: tokenResponse.refresh_token?.trim() || undefined,
        expiresAt: resolveExpiresAt(tokenResponse.expires_in),
      };
    }

    if (tokenResponse.error === 'authorization_pending') {
      continue;
    }
    if (tokenResponse.error === 'slow_down') {
      intervalMs += 5000;
      continue;
    }
    if (tokenResponse.error) {
      throw tokenErrorFromCode(tokenResponse.error, tokenResponse.error_description);
    }
  }

  throw new EntraDeviceCodeTokenProviderError(
    'ENTRA_DEVICE_AUTH_EXPIRED',
    'Device-code sign-in timed out before token acquisition completed.',
  );
}

export interface EntraRefreshTokenOptions {
  authority: string;
  clientId: string;
  refreshToken: string;
  scope?: string;
}

export async function refreshEntraAccessToken(
  options: EntraRefreshTokenOptions
): Promise<EntraAccessTokenBundle> {
  const authorityBase = normalizeEntraAuthority(options.authority);
  const tokenEndpoint = `${authorityBase}/oauth2/v2.0/token`;
  const clientId = options.clientId.trim();
  const refreshToken = options.refreshToken.trim();

  if (!clientId || !refreshToken) {
    throw new EntraDeviceCodeTokenProviderError(
      'ENTRA_DEVICE_TOKEN_FAILED',
      'Unable to refresh Entra access token due to missing refresh token metadata.',
    );
  }

  const payload = new URLSearchParams({
    grant_type: 'refresh_token',
    client_id: clientId,
    refresh_token: refreshToken,
  });

  const normalizedScope = options.scope?.trim();
  if (normalizedScope) {
    payload.set('scope', normalizedScope);
  }

  let tokenResponse: TokenResponse;
  try {
    tokenResponse = await postForm(tokenEndpoint, payload) as TokenResponse;
  } catch (error) {
    if (error instanceof EntraDeviceCodeTokenProviderError) {
      throw new EntraDeviceCodeTokenProviderError(
        'ENTRA_DEVICE_TOKEN_FAILED',
        'Failed to refresh Entra access token.',
        error.causeDetail ?? error.message,
      );
    }
    throw error;
  }

  if (!tokenResponse.access_token) {
    throw new EntraDeviceCodeTokenProviderError(
      'ENTRA_DEVICE_TOKEN_FAILED',
      'Entra token refresh response did not include an access token.',
    );
  }

  return {
    accessToken: tokenResponse.access_token,
    refreshToken: tokenResponse.refresh_token?.trim() || undefined,
    expiresAt: resolveExpiresAt(tokenResponse.expires_in),
  };
}

function resolveExpiresAt(expiresInSeconds: number | undefined): string | undefined {
  if (typeof expiresInSeconds !== 'number' || !Number.isFinite(expiresInSeconds) || expiresInSeconds <= 0) {
    return undefined;
  }

  return new Date(Date.now() + expiresInSeconds * 1000).toISOString();
}
