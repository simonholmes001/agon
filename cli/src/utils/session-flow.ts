import type { Message, SessionResponse } from '../api/types.js';

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
    return ordered.find(m => m.agentId === 'post_delivery_assistant');
  }

  return ordered.find(m => m.agentId !== 'moderator');
}

export function getLatestPostDeliveryAssistantMessage(
  messages: Message[],
  afterCreatedAt?: string
): Message | undefined {
  const ordered = [...messages]
    .filter(m => m.agentId === 'post_delivery_assistant')
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
