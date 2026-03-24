import { mkdtemp, rm } from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { AgentModelConfigManager } from '../../src/state/agent-model-config.js';

describe('AgentModelConfigManager', () => {
  const tempDirs: string[] = [];

  afterEach(async () => {
    await Promise.all(tempDirs.map((dir) => rm(dir, { recursive: true, force: true })));
  });

  async function createManager(userScope: string): Promise<AgentModelConfigManager> {
    const dir = await mkdtemp(path.join(os.tmpdir(), 'agon-agent-model-config-'));
    tempDirs.push(dir);
    return new AgentModelConfigManager(userScope, dir);
  }

  it('creates and persists default profile when none exists', async () => {
    const manager = await createManager('scope-a');
    const profile = await manager.ensurePersisted();

    expect(profile.userScope).toBe('scope-a');
    expect(profile.agentModels.moderator.provider).toBe('openai');
    expect(profile.agentModels.gemini_agent.provider).toBe('google');
    expect(manager.getProfilePath()).toContain('scope-a.json');
  });

  it('accepts gemini alias and normalizes it to google', async () => {
    const manager = await createManager('scope-a');
    const profile = await manager.setAgentModel('gemini_agent', 'gemini', 'gemini-2.5-flash');

    expect(profile.agentModels.gemini_agent.provider).toBe('google');
    expect(profile.agentModels.gemini_agent.model).toBe('gemini-2.5-flash');
  });

  it('returns required provider set based on current mapping', async () => {
    const manager = await createManager('scope-a');
    await manager.setAgentModel('synthesizer', 'deepseek', 'deepseek-chat');
    const providers = await manager.getRequiredProviders();

    expect(providers).toEqual(['anthropic', 'deepseek', 'google', 'openai']);
  });
});
