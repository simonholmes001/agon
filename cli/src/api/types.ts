/**
 * API Types - shared interfaces for backend communication
 */

export interface SessionResponse {
  id: string;
  status: SessionStatus;
  phase: SessionPhase;
  createdAt: string;
  updatedAt: string;
  convergence?: ConvergenceScore;
  currentRound?: number;
  tokensUsed?: number;
  tokenBudget?: number;
}

export type SessionStatus = 'active' | 'paused' | 'complete' | 'complete_with_gaps' | 'closed';

export type SessionPhase = 
  | 'Intake' 
  | 'Clarification' 
  | 'AnalysisRound' 
  | 'Critique' 
  | 'Synthesis' 
  | 'TargetedLoop' 
  | 'Deliver' 
  | 'DeliverWithGaps' 
  | 'PostDelivery';

export interface ConvergenceScore {
  overall: number;
  dimensions: {
    assumption_explicitness: number;
    evidence_quality: number;
    risk_coverage: number;
    decision_clarity: number;
    scope_definition: number;
    constraint_alignment: number;
    uncertainty_acknowledgment: number;
  };
}

export interface CreateSessionRequest {
  idea: string;
  friction?: number;
  researchEnabled?: boolean;
}

export interface Artifact {
  type: ArtifactType;
  content: string;
  version: number;
  createdAt: string;
}

export type ArtifactType = 
  | 'verdict' 
  | 'plan' 
  | 'prd' 
  | 'risks' 
  | 'assumptions' 
  | 'architecture' 
  | 'copilot';

export interface APIError {
  message: string;
  statusCode: number;
  errors?: Record<string, string[]>;
}

export interface Message {
  agentId: string;
  message: string;
  round: number;
  createdAt: string;
}

export interface SubmitMessageRequest {
  content: string;
}

export interface SessionAttachment {
  id: string;
  sessionId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  accessUrl: string;
  uploadedAt: string;
  hasExtractedText: boolean;
  extractedTextPreview?: string;
}
