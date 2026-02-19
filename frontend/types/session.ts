// ─── Session Types ───────────────────────────────────────────────────────────
// Mirrors the backend data model defined in schemas.instructions.md

export type SessionStatus =
  | "active"
  | "paused"
  | "complete"
  | "complete_with_gaps"
  | "forked"
  | "closed";

export type SessionMode = "quick" | "deep";

export type SessionPhase =
  | "INTAKE"
  | "CLARIFICATION"
  | "DEBATE_ROUND_1"
  | "DEBATE_ROUND_2"
  | "SYNTHESIS"
  | "TARGETED_LOOP"
  | "DELIVER"
  | "DELIVER_WITH_GAPS"
  | "POST_DELIVERY";

export interface Session {
  id: string;
  mode: SessionMode;
  frictionLevel: number;
  status: SessionStatus;
  phase: SessionPhase;
  forkedFrom: string | null;
  createdAt: string;
  updatedAt: string;
}

// ─── Truth Map Types ─────────────────────────────────────────────────────────

export type EntityStatus =
  | "active"
  | "contested"
  | "pending_revalidation"
  | "resolved";

export type AssumptionStatus = "unvalidated" | "validated" | "invalidated";

export type RiskCategory =
  | "market"
  | "technical"
  | "execution"
  | "security"
  | "financial";

export type Severity = "low" | "medium" | "high" | "critical";
export type Likelihood = "low" | "medium" | "high";

export interface Claim {
  id: string;
  agent: string;
  round: number;
  text: string;
  confidence: number;
  status: EntityStatus;
  derivedFrom: string[];
  challengedBy: string[];
}

export interface Assumption {
  id: string;
  text: string;
  validationStep: string;
  derivedFrom: string[];
  status: AssumptionStatus;
}

export interface Decision {
  id: string;
  text: string;
  rationale: string;
  owner: string;
  derivedFrom: string[];
  binding: boolean;
}

export interface Risk {
  id: string;
  text: string;
  category: RiskCategory;
  severity: Severity;
  likelihood: Likelihood;
  mitigation: string;
  derivedFrom: string[];
  agent: string;
}

export interface OpenQuestion {
  id: string;
  text: string;
  blocking: boolean;
  raisedBy: string;
}

export interface Evidence {
  id: string;
  title: string;
  source: string;
  retrievedAt: string;
  summary: string;
  supports: string[];
  contradicts: string[];
}

export interface Convergence {
  claritySpecificity: number;
  feasibility: number;
  riskCoverage: number;
  assumptionExplicitness: number;
  coherence: number;
  actionability: number;
  evidenceQuality: number;
  overall: number;
  threshold: number;
  status: "in_progress" | "converged" | "gaps_remain";
}

export interface TruthMap {
  sessionId: string;
  version: number;
  round: number;
  coreIdea: string;
  constraints: {
    budget: string;
    timeline: string;
    techStack: string[];
    nonNegotiables: string[];
  };
  successMetrics: string[];
  personas: { id: string; name: string; description: string }[];
  claims: Claim[];
  assumptions: Assumption[];
  decisions: Decision[];
  risks: Risk[];
  openQuestions: OpenQuestion[];
  evidence: Evidence[];
  convergence: Convergence;
}

// ─── Agent Types ─────────────────────────────────────────────────────────────

export type AgentId =
  | "socratic-clarifier"
  | "framing-challenger"
  | "product-strategist"
  | "technical-architect"
  | "contrarian"
  | "research-librarian"
  | "synthesis-validation";

export interface AgentInfo {
  id: AgentId;
  name: string;
  shortName: string;
  role: string;
  model: string;
  color: string;
  icon: string;
}

// ─── Message Types ───────────────────────────────────────────────────────────

export interface AgentMessage {
  id: string;
  sessionId: string;
  agentId: AgentId;
  round: number;
  phase: SessionPhase;
  content: string;
  isStreaming: boolean;
  createdAt: string;
}

export interface UserMessage {
  id: string;
  sessionId: string;
  content: string;
  createdAt: string;
}

export type ThreadMessage =
  | { type: "agent"; message: AgentMessage }
  | { type: "user"; message: UserMessage }
  | { type: "system"; message: { id: string; content: string; createdAt: string } };

// ─── Artifact Types ──────────────────────────────────────────────────────────

export type ArtifactType =
  | "verdict"
  | "plan"
  | "prd"
  | "risks"
  | "assumptions"
  | "copilot"
  | "architecture"
  | "scenario_diff";

export interface Artifact {
  id: string;
  sessionId: string;
  type: ArtifactType;
  content: string;
  version: number;
  createdAt: string;
}
