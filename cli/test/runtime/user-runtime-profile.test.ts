import { describe, expect, it, vi, beforeEach } from 'vitest';

const mockEnsurePersisted = vi.hoisted(() => vi.fn());
const mockGetKey = vi.hoisted(() => vi.fn());
const mockResolveUserScope = vi.hoisted(() => vi.fn(() => 'auth_scope'));

vi.mock('../../src/state/agent-model-config.js', () => ({
  AGENT_MODEL_IDS: [
    'moderator',
    'gpt_agent',
    'gemini_agent',
    'claude_agent',
    'synthesizer',
    'post_delivery_assistant',
  ],
  AgentModelConfigManager: vi.fn(function () {
    return {
      ensurePersisted: mockEnsurePersisted,
    };
  }),
}));

vi.mock('../../src/auth/api-key-manager.js', () => ({
  ApiKeyManager: vi.fn(function () {
    return {
      get: mockGetKey,
    };
  }),
}));

vi.mock('../../src/auth/user-scope.js', () => ({
  resolveUserScope: mockResolveUserScope,
}));

import {
  buildRuntimeExecutionProfile,
  encodeAgentModelHeader,
} from '../../src/runtime/user-runtime-profile.js';

describe('user runtime profile', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('builds runtime profile and reports missing provider keys', async () => {
    mockEnsurePersisted.mockResolvedValue({
      userScope: 'auth_scope',
      agentModels: {
        moderator: { provider: 'openai', model: 'gpt-5.2' },
        gpt_agent: { provider: 'openai', model: 'gpt-5.2' },
        gemini_agent: { provider: 'google', model: 'gemini-3-flash-preview' },
        claude_agent: { provider: 'anthropic', model: 'claude-opus-4-6' },
        synthesizer: { provider: 'openai', model: 'gpt-5.2' },
        post_delivery_assistant: { provider: 'openai', model: 'gpt-5.2' },
      },
    });

    mockGetKey.mockImplementation(async (provider: string) => {
      if (provider === 'openai') return 'sk-openai';
      if (provider === 'anthropic') return 'sk-anthropic';
      return null;
    });

    const result = await buildRuntimeExecutionProfile('token');

    expect(result.profile.userScope).toBe('auth_scope');
    expect(result.profile.providerKeys).toEqual({
      openai: 'sk-openai',
      anthropic: 'sk-anthropic',
    });
    expect(result.missingProviders).toEqual(['google']);
  });

  it('encodes agent model header as base64url json', () => {
    const encoded = encodeAgentModelHeader({
      moderator: { provider: 'openai', model: 'gpt-5.2' },
      gpt_agent: { provider: 'openai', model: 'gpt-5.2' },
      gemini_agent: { provider: 'google', model: 'gemini-3-flash-preview' },
      claude_agent: { provider: 'anthropic', model: 'claude-opus-4-6' },
      synthesizer: { provider: 'openai', model: 'gpt-5.2' },
      post_delivery_assistant: { provider: 'openai', model: 'gpt-5.2' },
    });

    const decoded = JSON.parse(Buffer.from(encoded, 'base64url').toString('utf8'));
    expect(decoded.moderator.model).toBe('gpt-5.2');
    expect(decoded.gemini_agent.provider).toBe('google');
  });
});
