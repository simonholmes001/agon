import type { AgentId, AgentInfo } from "@/types";

export const AGENTS: Record<AgentId, AgentInfo> = {
  "socratic-clarifier": {
    id: "socratic-clarifier",
    name: "Socratic Clarifier",
    shortName: "Socratic",
    role: "Clarifies intent, constraints, and success metrics",
    model: "GPT-5.2 Thinking",
    color: "#6366f1", // indigo
    icon: "🏛️",
  },
  "framing-challenger": {
    id: "framing-challenger",
    name: "Framing Challenger",
    shortName: "Framing",
    role: "Challenges whether the right problem is being solved",
    model: "Gemini 3",
    color: "#f59e0b", // amber
    icon: "🔍",
  },
  "product-strategist": {
    id: "product-strategist",
    name: "Product Strategist",
    shortName: "Product",
    role: "Maximises user value and market fit",
    model: "Claude Opus 4.6",
    color: "#10b981", // emerald
    icon: "🎯",
  },
  "technical-architect": {
    id: "technical-architect",
    name: "Technical Architect",
    shortName: "Architect",
    role: "Proposes feasible architecture and identifies technical risks",
    model: "GPT-5.2 Thinking",
    color: "#3b82f6", // blue
    icon: "⚙️",
  },
  contrarian: {
    id: "contrarian",
    name: "Contrarian / Red Team",
    shortName: "Contrarian",
    role: "Exposes failure modes, fallacies, and hidden risks",
    model: "Gemini 3",
    color: "#ef4444", // red
    icon: "⚡",
  },
  "research-librarian": {
    id: "research-librarian",
    name: "Research Librarian",
    shortName: "Research",
    role: "Finds and stores verifiable external evidence",
    model: "GPT-5.2 Thinking",
    color: "#8b5cf6", // violet
    icon: "📚",
  },
  "synthesis-validation": {
    id: "synthesis-validation",
    name: "Synthesis + Validation",
    shortName: "Synthesis",
    role: "Unifies into coherent plan and scores convergence",
    model: "GPT-5.2 Thinking",
    color: "#06b6d4", // cyan
    icon: "🧬",
  },
};

export const PHASE_LABELS: Record<string, string> = {
  INTAKE: "Starting",
  CLARIFICATION: "Clarifying",
  DEBATE_ROUND_1: "Round 1 — Divergence",
  DEBATE_ROUND_2: "Round 2 — Crossfire",
  SYNTHESIS: "Synthesising",
  TARGETED_LOOP: "Targeted Loop",
  DELIVER: "Delivering",
  DELIVER_WITH_GAPS: "Delivering (with gaps)",
  POST_DELIVERY: "Post-Delivery",
};

export const FRICTION_LABELS = [
  { min: 0, max: 30, label: "Brainstorm", description: "Yes-and tone, low convergence bar" },
  { min: 31, max: 70, label: "Balanced", description: "Challenge with alternatives" },
  { min: 71, max: 100, label: "Adversarial", description: "Red-team, high evidence bar" },
] as const;

export function getFrictionLabel(level: number) {
  return FRICTION_LABELS.find((f) => level >= f.min && level <= f.max) ?? FRICTION_LABELS[1];
}
