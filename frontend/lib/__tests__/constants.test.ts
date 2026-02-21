import { describe, it, expect } from "vitest";
import { AGENTS, PHASE_LABELS, FRICTION_LABELS, getFrictionLabel } from "@/lib/constants";

describe("AGENTS registry", () => {
  it("contains all seven council agents", () => {
    const agentIds = Object.keys(AGENTS);
    expect(agentIds).toHaveLength(7);
    expect(agentIds).toContain("socratic-clarifier");
    expect(agentIds).toContain("framing-challenger");
    expect(agentIds).toContain("product-strategist");
    expect(agentIds).toContain("technical-architect");
    expect(agentIds).toContain("contrarian");
    expect(agentIds).toContain("research-librarian");
    expect(agentIds).toContain("synthesis-validation");
  });

  it("every agent has all required fields", () => {
    for (const agent of Object.values(AGENTS)) {
      expect(agent.id).toBeTruthy();
      expect(agent.name).toBeTruthy();
      expect(agent.shortName).toBeTruthy();
      expect(agent.role).toBeTruthy();
      expect(agent.model).toBeTruthy();
      expect(agent.color).toMatch(/^#[0-9a-f]{6}$/i);
      expect(agent.icon).toBeTruthy();
    }
  });

  it("agent IDs match their registry keys", () => {
    for (const [key, agent] of Object.entries(AGENTS)) {
      expect(agent.id).toBe(key);
    }
  });

  it("maps models according to the architecture spec", () => {
    expect(AGENTS["socratic-clarifier"].model).toBe("GPT-5.2 Thinking");
    expect(AGENTS["framing-challenger"].model).toBe("Gemini 3");
    expect(AGENTS["product-strategist"].model).toBe("Claude Opus 4.6");
    expect(AGENTS["technical-architect"].model).toBe("GPT-5.2 Thinking");
    expect(AGENTS["contrarian"].model).toBe("Gemini 3");
    expect(AGENTS["research-librarian"].model).toBe("GPT-5.2 Thinking");
    expect(AGENTS["synthesis-validation"].model).toBe("GPT-5.2 Thinking");
  });
});

describe("PHASE_LABELS", () => {
  it("has a label for every session phase", () => {
    const expectedPhases = [
      "INTAKE",
      "CLARIFICATION",
      "DEBATE_ROUND_1",
      "DEBATE_ROUND_2",
      "SYNTHESIS",
      "TARGETED_LOOP",
      "DELIVER",
      "DELIVER_WITH_GAPS",
      "POST_DELIVERY",
    ];
    for (const phase of expectedPhases) {
      expect(PHASE_LABELS[phase]).toBeTruthy();
    }
  });
});

describe("FRICTION_LABELS", () => {
  it("covers the full 0–100 range without gaps", () => {
    expect(FRICTION_LABELS).toHaveLength(3);
    expect(FRICTION_LABELS[0].min).toBe(0);
    expect(FRICTION_LABELS[2].max).toBe(100);
    // No gaps between ranges
    expect(FRICTION_LABELS[1].min).toBe(FRICTION_LABELS[0].max + 1);
    expect(FRICTION_LABELS[2].min).toBe(FRICTION_LABELS[1].max + 1);
  });
});

describe("getFrictionLabel", () => {
  it("returns Brainstorm for friction 0–30", () => {
    expect(getFrictionLabel(0).label).toBe("Brainstorm");
    expect(getFrictionLabel(15).label).toBe("Brainstorm");
    expect(getFrictionLabel(30).label).toBe("Brainstorm");
  });

  it("returns Balanced for friction 31–70", () => {
    expect(getFrictionLabel(31).label).toBe("Balanced");
    expect(getFrictionLabel(50).label).toBe("Balanced");
    expect(getFrictionLabel(70).label).toBe("Balanced");
  });

  it("returns Adversarial for friction 71–100", () => {
    expect(getFrictionLabel(71).label).toBe("Adversarial");
    expect(getFrictionLabel(85).label).toBe("Adversarial");
    expect(getFrictionLabel(100).label).toBe("Adversarial");
  });

  it("falls back to Balanced for out-of-range values", () => {
    expect(getFrictionLabel(-1).label).toBe("Balanced");
    expect(getFrictionLabel(101).label).toBe("Balanced");
  });
});
