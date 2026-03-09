import type { SessionResponse } from '../api/types.js';
import {
  isClarificationPhase,
  isMidDebatePhase,
  isPostDeliveryPhase,
  isSessionClosed
} from '../utils/session-flow.js';

export type PlainInputRoute =
  | { action: 'start' }
  | { action: 'follow-up' }
  | { action: 'blocked'; reason: string };

const BLOCKED_REASON = 'Debate is still in progress. Use /status, wait for completion, or run /new to start another idea.';

export function routePlainInput(session: SessionResponse | null): PlainInputRoute {
  if (!session) {
    return { action: 'start' };
  }

  if (isSessionClosed(session)) {
    return { action: 'start' };
  }

  if (isClarificationPhase(session.phase) || isPostDeliveryPhase(session.phase)) {
    return { action: 'follow-up' };
  }

  if (isMidDebatePhase(session.phase)) {
    return {
      action: 'blocked',
      reason: BLOCKED_REASON
    };
  }

  return { action: 'start' };
}
