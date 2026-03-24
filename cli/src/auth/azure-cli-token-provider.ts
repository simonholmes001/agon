import { execFile } from 'node:child_process';
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export interface AzureCliTokenOptions {
  scope: string;
  tenant?: string;
  interactiveLogin?: boolean;
}

export class AzureCliTokenProviderError extends Error {
  readonly code: 'AZURE_CLI_NOT_FOUND' | 'AZURE_CLI_LOGIN_REQUIRED' | 'AZURE_CLI_TOKEN_FAILED' | 'INVALID_SCOPE';

  constructor(
    code: AzureCliTokenProviderError['code'],
    message: string,
    readonly causeDetail?: string,
  ) {
    super(message);
    this.code = code;
  }
}

export function normalizeAzureScope(rawScope: string): string {
  const scope = rawScope.trim();
  if (!scope) {
    throw new AzureCliTokenProviderError(
      'INVALID_SCOPE',
      'Azure scope must not be empty.',
      'Provide a scope like api://<api-app-id>/.default',
    );
  }

  if (scope.endsWith('/.default')) {
    return scope;
  }

  if (uuidPattern.test(scope)) {
    return `api://${scope}/.default`;
  }

  if (scope.startsWith('api://') || scope.includes('://')) {
    return `${scope}/.default`;
  }

  return `api://${scope}/.default`;
}

async function runAz(args: string[]): Promise<string> {
  return new Promise<string>((resolve, reject) => {
    execFile(
      'az',
      args,
      { maxBuffer: 1024 * 1024 * 4 },
      (error, stdout, stderr) => {
        if (error) {
          const err = error as NodeJS.ErrnoException;
          if (err.code === 'ENOENT') {
            reject(new AzureCliTokenProviderError(
              'AZURE_CLI_NOT_FOUND',
              'Azure CLI (az) was not found on PATH.',
              'Install Azure CLI: https://aka.ms/azure-cli',
            ));
            return;
          }

          reject(new AzureCliTokenProviderError(
            'AZURE_CLI_TOKEN_FAILED',
            'Azure CLI command failed.',
            (stderr || '').toString().trim() || err.message,
          ));
          return;
        }

        resolve((stdout || '').toString().trim());
      },
    );
  });
}

async function ensureAzureAccount(interactiveLogin: boolean, tenant?: string): Promise<void> {
  const requestedTenant = tenant?.trim();
  const needsTenantSpecificLogin = async (): Promise<boolean> => {
    if (!requestedTenant) {
      return false;
    }

    try {
      const activeTenant = (await runAz(['account', 'show', '--query', 'tenantId', '-o', 'tsv'])).trim();
      return !activeTenant || activeTenant.toLowerCase() !== requestedTenant.toLowerCase();
    } catch {
      return true;
    }
  };

  try {
    await runAz(['account', 'show', '--output', 'none']);
    if (!(await needsTenantSpecificLogin())) {
      return;
    }
  } catch (error) {
    const err = error as AzureCliTokenProviderError;
    if (err.code === 'AZURE_CLI_NOT_FOUND') {
      throw err;
    }

    if (!interactiveLogin) {
      throw new AzureCliTokenProviderError(
        'AZURE_CLI_LOGIN_REQUIRED',
        'Azure CLI is not logged in.',
        'Run `az login` first, then retry `agon login --azure-cli --scope <scope>`.',
      );
    }
  }

  if (!interactiveLogin) {
    throw new AzureCliTokenProviderError(
      'AZURE_CLI_LOGIN_REQUIRED',
      'Azure CLI is logged into a different tenant than requested.',
      requestedTenant
        ? `Run \`az login --tenant "${requestedTenant}"\` and retry \`agon login --azure-cli --scope <scope> --tenant "${requestedTenant}"\`.`
        : 'Run `az login` and retry.',
    );
  }

  try {
    const loginArgs = ['login', '--use-device-code', '--output', 'none'];
    if (requestedTenant) {
      loginArgs.push('--tenant', requestedTenant);
    }
    await runAz(loginArgs);
    await runAz(['account', 'show', '--output', 'none']);
  } catch (error) {
    const err = error as AzureCliTokenProviderError;
    if (err.code === 'AZURE_CLI_NOT_FOUND') {
      throw err;
    }

    throw new AzureCliTokenProviderError(
      'AZURE_CLI_LOGIN_REQUIRED',
      'Unable to authenticate with Azure CLI.',
      err.causeDetail,
    );
  }
}

export async function acquireTokenFromAzureCli(options: AzureCliTokenOptions): Promise<string> {
  const scope = normalizeAzureScope(options.scope);
  await ensureAzureAccount(options.interactiveLogin ?? false, options.tenant);

  const args = [
    'account',
    'get-access-token',
    '--scope',
    scope,
    '--query',
    'accessToken',
    '-o',
    'tsv',
  ];
  if (options.tenant?.trim()) {
    args.push('--tenant', options.tenant.trim());
  }

  let token: string;
  try {
    token = await runAz(args);
  } catch (error) {
    const err = error as AzureCliTokenProviderError;
    if (err.code === 'AZURE_CLI_NOT_FOUND') {
      throw err;
    }
    throw new AzureCliTokenProviderError(
      'AZURE_CLI_TOKEN_FAILED',
      'Failed to obtain access token from Azure CLI.',
      err.causeDetail,
    );
  }

  if (!token) {
    throw new AzureCliTokenProviderError(
      'AZURE_CLI_TOKEN_FAILED',
      'Azure CLI returned an empty access token.',
      'Verify the provided scope and Azure account permissions.',
    );
  }

  return token;
}
