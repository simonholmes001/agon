import { constants as fsConstants, promises as fs } from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';

export const AGENT_MODEL_IDS = [
  'moderator',
  'gpt_agent',
  'gemini_agent',
  'claude_agent',
  'synthesizer',
  'post_delivery_assistant',
] as const;

export type AgentModelId = (typeof AGENT_MODEL_IDS)[number];
export type ProviderId = 'openai' | 'anthropic' | 'google' | 'deepseek';

export interface AgentModelSelection {
  provider: ProviderId;
  model: string;
  updatedAt: string;
}

export interface AgentModelProfile {
  schemaVersion: 1;
  userScope: string;
  createdAt: string;
  updatedAt: string;
  agentModels: Record<AgentModelId, AgentModelSelection>;
}

export const DEFAULT_AGENT_MODELS: Record<AgentModelId, { provider: ProviderId; model: string }> = {
  moderator: { provider: 'openai', model: 'gpt-5.2' },
  gpt_agent: { provider: 'openai', model: 'gpt-5.2' },
  gemini_agent: { provider: 'google', model: 'gemini-3-flash-preview' },
  claude_agent: { provider: 'anthropic', model: 'claude-opus-4-6' },
  synthesizer: { provider: 'openai', model: 'gpt-5.2' },
  post_delivery_assistant: { provider: 'openai', model: 'gpt-5.2' },
};

function defaultProfilesDirectory(): string {
  return path.join(os.homedir(), '.agon', 'profiles');
}

function normalizeProvider(value: string): ProviderId {
  const normalized = value.trim().toLowerCase();
  if (normalized === 'gemini') {
    return 'google';
  }

  if (normalized === 'openai' || normalized === 'anthropic' || normalized === 'google' || normalized === 'deepseek') {
    return normalized;
  }

  throw new Error(
    `Unsupported provider "${value}". Valid providers: openai, anthropic, google (alias: gemini), deepseek.`,
  );
}

function normalizeModel(value: string): string {
  const normalized = value.trim();
  if (!normalized) {
    throw new Error('Model name must not be empty.');
  }

  return normalized;
}

function normalizeAgentId(agentId: string): AgentModelId {
  const normalized = agentId.trim().toLowerCase();
  if ((AGENT_MODEL_IDS as readonly string[]).includes(normalized)) {
    return normalized as AgentModelId;
  }

  throw new Error(
    `Unsupported agent "${agentId}". Valid agents: ${AGENT_MODEL_IDS.join(', ')}.`,
  );
}

function createDefaultProfile(userScope: string): AgentModelProfile {
  const now = new Date().toISOString();
  return {
    schemaVersion: 1,
    userScope,
    createdAt: now,
    updatedAt: now,
    agentModels: {
      moderator: { ...DEFAULT_AGENT_MODELS.moderator, updatedAt: now },
      gpt_agent: { ...DEFAULT_AGENT_MODELS.gpt_agent, updatedAt: now },
      gemini_agent: { ...DEFAULT_AGENT_MODELS.gemini_agent, updatedAt: now },
      claude_agent: { ...DEFAULT_AGENT_MODELS.claude_agent, updatedAt: now },
      synthesizer: { ...DEFAULT_AGENT_MODELS.synthesizer, updatedAt: now },
      post_delivery_assistant: {
        ...DEFAULT_AGENT_MODELS.post_delivery_assistant,
        updatedAt: now,
      },
    },
  };
}

export class AgentModelConfigManager {
  private readonly userScope: string;
  private readonly profilesDir: string;

  constructor(userScope: string, profilesDir?: string) {
    this.userScope = userScope.trim();
    if (!this.userScope) {
      throw new Error('User scope must not be empty.');
    }

    this.profilesDir = profilesDir ?? defaultProfilesDirectory();
  }

  getProfilePath(): string {
    return path.join(this.profilesDir, `${this.userScope}.json`);
  }

  async load(): Promise<AgentModelProfile> {
    const filePath = this.getProfilePath();

    try {
      const raw = await fs.readFile(filePath, 'utf-8');
      const parsed = JSON.parse(raw) as Partial<AgentModelProfile>;
      return this.normalizeParsedProfile(parsed);
    } catch (error) {
      const err = error as NodeJS.ErrnoException;
      if (err.code === 'ENOENT') {
        return createDefaultProfile(this.userScope);
      }

      throw error;
    }
  }

  async save(profile: AgentModelProfile): Promise<void> {
    const filePath = this.getProfilePath();
    await fs.mkdir(path.dirname(filePath), { recursive: true, mode: 0o700 });
    await fs.writeFile(filePath, JSON.stringify(profile, null, 2), {
      encoding: 'utf-8',
      mode: 0o600,
    });
  }

  async ensurePersisted(): Promise<AgentModelProfile> {
    const profile = await this.load();
    const filePath = this.getProfilePath();
    try {
      await fs.access(filePath, fsConstants.F_OK);
    } catch {
      await this.save(profile);
    }

    return profile;
  }

  async setAgentModel(agentId: string, provider: string, model: string): Promise<AgentModelProfile> {
    const normalizedAgent = normalizeAgentId(agentId);
    const normalizedProvider = normalizeProvider(provider);
    const normalizedModel = normalizeModel(model);

    const profile = await this.load();
    const now = new Date().toISOString();

    profile.agentModels[normalizedAgent] = {
      provider: normalizedProvider,
      model: normalizedModel,
      updatedAt: now,
    };
    profile.updatedAt = now;

    await this.save(profile);
    return profile;
  }

  async getAgentModel(agentId: string): Promise<AgentModelSelection> {
    const normalizedAgent = normalizeAgentId(agentId);
    const profile = await this.load();
    return profile.agentModels[normalizedAgent];
  }

  async listAgentModels(): Promise<Record<AgentModelId, AgentModelSelection>> {
    const profile = await this.load();
    return profile.agentModels;
  }

  async getRequiredProviders(): Promise<ProviderId[]> {
    const profile = await this.load();
    const providers = new Set<ProviderId>();
    for (const agentId of AGENT_MODEL_IDS) {
      providers.add(profile.agentModels[agentId].provider);
    }

    return [...providers].sort();
  }

  async isComplete(): Promise<boolean> {
    const profile = await this.load();
    for (const agentId of AGENT_MODEL_IDS) {
      const selection = profile.agentModels[agentId];
      if (!selection?.provider || !selection?.model) {
        return false;
      }
    }

    return true;
  }

  private normalizeParsedProfile(parsed: Partial<AgentModelProfile>): AgentModelProfile {
    const baseline = createDefaultProfile(this.userScope);
    const createdAt = typeof parsed.createdAt === 'string' && parsed.createdAt ? parsed.createdAt : baseline.createdAt;
    const updatedAt = typeof parsed.updatedAt === 'string' && parsed.updatedAt ? parsed.updatedAt : baseline.updatedAt;
    const models = parsed.agentModels ?? {};

    const normalizedModels = {} as Record<AgentModelId, AgentModelSelection>;
    for (const agentId of AGENT_MODEL_IDS) {
      const candidate = models[agentId as keyof typeof models] as Partial<AgentModelSelection> | undefined;
      const fallback = baseline.agentModels[agentId];

      let provider: ProviderId;
      try {
        provider = candidate?.provider ? normalizeProvider(candidate.provider) : fallback.provider;
      } catch {
        provider = fallback.provider;
      }

      const model = candidate?.model?.trim() ? candidate.model.trim() : fallback.model;
      const entryUpdatedAt = typeof candidate?.updatedAt === 'string' && candidate.updatedAt
        ? candidate.updatedAt
        : updatedAt;

      normalizedModels[agentId] = {
        provider,
        model,
        updatedAt: entryUpdatedAt,
      };
    }

    return {
      schemaVersion: 1,
      userScope: this.userScope,
      createdAt,
      updatedAt,
      agentModels: normalizedModels,
    };
  }
}
