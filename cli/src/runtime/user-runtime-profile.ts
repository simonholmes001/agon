import {
  AGENT_MODEL_IDS,
  AgentModelConfigManager,
  type AgentModelId,
  type ProviderId,
} from '../state/agent-model-config.js';
import { ApiKeyManager } from '../auth/api-key-manager.js';
import { resolveUserScope } from '../auth/user-scope.js';

export interface RuntimeAgentModelConfig {
  provider: ProviderId;
  model: string;
}

export interface RuntimeExecutionProfile {
  userScope: string;
  agentModels: Record<AgentModelId, RuntimeAgentModelConfig>;
  providerKeys: Partial<Record<ProviderId, string>>;
}

export interface RuntimeProfileResult {
  profile: RuntimeExecutionProfile;
  missingProviders: ProviderId[];
}

export async function buildRuntimeExecutionProfile(
  token?: string | null,
): Promise<RuntimeProfileResult> {
  const userScope = resolveUserScope(token);
  const modelManager = new AgentModelConfigManager(userScope);
  const keyManager = new ApiKeyManager({ userScope });

  const profile = await modelManager.ensurePersisted();
  const agentModels = {} as Record<AgentModelId, RuntimeAgentModelConfig>;
  const requiredProviders = new Set<ProviderId>();

  for (const agentId of AGENT_MODEL_IDS) {
    const selection = profile.agentModels[agentId];
    agentModels[agentId] = {
      provider: selection.provider,
      model: selection.model,
    };
    requiredProviders.add(selection.provider);
  }

  const providerKeys: Partial<Record<ProviderId, string>> = {};
  const missingProviders: ProviderId[] = [];

  for (const provider of [...requiredProviders]) {
    const key = await keyManager.get(provider);
    if (!key) {
      missingProviders.push(provider);
      continue;
    }

    providerKeys[provider] = key;
  }

  return {
    profile: {
      userScope,
      agentModels,
      providerKeys,
    },
    missingProviders: missingProviders.sort(),
  };
}

export function encodeAgentModelHeader(
  models: Record<AgentModelId, RuntimeAgentModelConfig>,
): string {
  return Buffer.from(JSON.stringify(models), 'utf8').toString('base64url');
}
