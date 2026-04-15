import type { Message, SessionResponse } from '../api/types.js';

const postDeliveryResponseAgentIds = new Set([
  'post_delivery_assistant',
  'synthesizer'
]);

export function normalizePhase(phase: string): string {
  return phase.replace(/[\s_-]/g, '').toLowerCase();
}

export function normalizeStatus(status: string): string {
  const compact = status.replace(/[\s_-]/g, '').toLowerCase();
  if (compact === 'completewithgaps') return 'complete_with_gaps';
  return compact;
}

export function isClarificationPhase(phase: string): boolean {
  return normalizePhase(phase) === 'clarification';
}

export function isPostDeliveryPhase(phase: string): boolean {
  const normalized = normalizePhase(phase);
  return normalized === 'deliver'
    || normalized === 'deliverwithgaps'
    || normalized === 'postdelivery';
}

export function isMidDebatePhase(phase: string): boolean {
  const normalized = normalizePhase(phase);
  return normalized === 'analysisround'
    || normalized === 'critique'
    || normalized === 'synthesis'
    || normalized === 'targetedloop';
}

export function getLatestResponseMessage(phase: string, messages: Message[]): Message | undefined {
  const normalized = normalizePhase(phase);
  const ordered = [...messages].sort(
    (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
  );

  if (normalized === 'clarification') {
    return ordered.find(m => m.agentId === 'moderator');
  }

  if (isPostDeliveryPhase(phase)) {
    return getLatestPostDeliveryResponseMessage(messages);
  }

  return ordered.find(m => {
    const agent = m.agentId.toLowerCase();
    return agent !== 'moderator' && agent !== 'user';
  });
}

export function getLatestResponseMessageAfter(
  phase: string,
  messages: Message[],
  afterCreatedAt?: string
): Message | undefined {
  const latest = getLatestResponseMessage(phase, messages);
  if (!latest || !afterCreatedAt) {
    return latest;
  }

  return new Date(latest.createdAt).getTime() > new Date(afterCreatedAt).getTime()
    ? latest
    : undefined;
}

export function getLatestPostDeliveryAssistantMessage(
  messages: Message[],
  afterCreatedAt?: string
): Message | undefined {
  return getLatestPostDeliveryResponseMessage(messages, afterCreatedAt);
}

export function getLatestPostDeliveryResponseMessage(
  messages: Message[],
  afterCreatedAt?: string
): Message | undefined {
  const ordered = [...messages]
    .filter(m => postDeliveryResponseAgentIds.has(m.agentId))
    .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());

  if (!afterCreatedAt) {
    return ordered[0];
  }

  const threshold = new Date(afterCreatedAt).getTime();
  return ordered.find(m => new Date(m.createdAt).getTime() > threshold);
}

export function isSessionClosed(session: SessionResponse): boolean {
  return normalizeStatus(session.status) === 'closed';
}
