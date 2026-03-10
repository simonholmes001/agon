import { describe, expect, it } from 'vitest';
import type { SessionResponse } from '../../src/api/types.js';
import { routePlainInput } from '../../src/shell/router.js';

function buildSession(phase: string, status: SessionResponse['status'] = 'active'): SessionResponse {
  return {
    id: 'session-1',
    status,
    phase: phase as SessionResponse['phase'],
    createdAt: '2026-03-10T10:00:00Z',
    updatedAt: '2026-03-10T10:00:00Z'
  };
}

describe('shell router', () => {
  it('routes to start when there is no active session', () => {
    expect(routePlainInput(null)).toEqual({
      action: 'start'
    });
  });

  it('routes clarification sessions to follow-up', () => {
    expect(routePlainInput(buildSession('Clarification'))).toEqual({
      action: 'follow-up'
    });
  });

  it('routes delivered sessions to follow-up', () => {
    expect(routePlainInput(buildSession('Deliver', 'complete'))).toEqual({
      action: 'follow-up'
    });
    expect(routePlainInput(buildSession('DeliverWithGaps', 'complete_with_gaps'))).toEqual({
      action: 'follow-up'
    });
    expect(routePlainInput(buildSession('PostDelivery', 'active'))).toEqual({
      action: 'follow-up'
    });
  });

  it('blocks mid-debate phases', () => {
    expect(routePlainInput(buildSession('AnalysisRound'))).toEqual({
      action: 'blocked',
      reason: 'Debate is still in progress. Use /status, wait for completion, or run /new to start another idea.'
    });
    expect(routePlainInput(buildSession('Critique'))).toEqual({
      action: 'blocked',
      reason: 'Debate is still in progress. Use /status, wait for completion, or run /new to start another idea.'
    });
  });

  it('routes closed sessions to start', () => {
    expect(routePlainInput(buildSession('Deliver', 'closed'))).toEqual({
      action: 'start'
    });
  });

  it('routes unknown phases to start as a safe fallback', () => {
    expect(routePlainInput(buildSession('WeirdPhase'))).toEqual({
      action: 'start'
    });
  });
});
