---
applyTo: '**'
---
# Product Requirements Document: Agon (Living Strategy Room)

**Version:** 2.0  
**Status:** Updated — multi-model thinking council, living workspace design, improved convergence model  
**Stack:** Next.js (web) + .NET (backend) + Microsoft Agent Framework (orchestration)

---

## 1) Executive Summary

Agon is an agentic **idea analysis workspace** — a "living strategy room" where a user brings an idea and a council of specialist agents debates, challenges, and develops it into a decision-grade output pack.

Unlike a linear "input → debate → output" pipeline, Agon maintains a continuously updated **Global Workspace** ("Truth Map") that all agents read from and write to. If a constraint changes mid-session, the system updates the state and agents immediately re-evaluate their prior claims. The output is not just a transcript — it is a versioned, auditable, semantically linked graph of claims, assumptions, risks, and decisions.

---

## 2) Goals

### Primary goals
- Convert vague ideas into **clear problem definitions**, **explicit assumptions**, and **actionable plans**.
- Reduce blind spots using **model diversity** (different providers, different failure modes).
- Keep debate **bounded**: fixed round limits + budget limits + convergence scoring with friction-adjusted thresholds.
- Produce exportable artifacts: **Verdict**, **Plan**, **PRD**, **Risk Registry**, **Copilot instructions**.

### Secondary goals
- Make debate **steerable** (Human-in-the-loop interventions with defined change propagation).
- Provide **modes of friction** (brainstorm vs adversarial teardown), affecting not just tone but convergence thresholds.
- Support iteration and diffs across versions ("Idea v1 vs v2").
- Enable **Pause-and-Replay** — fork a session at any Truth Map snapshot, change a constraint or assumption, and re-run debate forward from that point.

---

## 3) Core Concepts

### 3.1 The Global Workspace ("Truth Map")

A structured, shared state updated throughout the session. The Truth Map is the **source of truth** — the transcript is merely provenance/evidence.

All entities carry a `derived_from` field linking them to parent claims, assumptions, or decisions. This enables the Orchestrator to trace contradictions and perform targeted invalidation when the Truth Map changes.

```
Truth Map Entities:
- core_idea
- constraints         (budget, time, stack, policies)
- success_metrics
- personas
- claims              { id, agent, text, confidence, derived_from: [id], challenged_by: [id] }
- assumptions         { id, text, validation_step, derived_from: [id] }
- decisions           { id, text, rationale, owner, derived_from: [id] }
- risks               { id, text, severity, likelihood, mitigation, derived_from: [id] }
- open_questions      { id, text, blocking: bool }
- evidence            { id, title, source, retrieved_at, supports: [id] }
- convergence         { clarity, feasibility, risk_coverage, assumption_explicitness,
                        coherence, actionability, evidence_quality, overall }
```

Agents do not just "chat" — they **propose patches** to the Truth Map using a structured `TruthMapPatch` operation log.

### 3.2 Confidence Decay

Claim confidence is **dynamic**, not static:
- **Initial confidence** is set by the authoring agent at claim creation.
- **Decay trigger**: if the Contrarian challenges a claim and no agent defends it within the same round, confidence decays by a configurable step (default: `-0.15`).
- **Boost trigger**: if the Research Librarian provides corroborating evidence linked via `evidence.supports`, confidence increases by a configurable step (default: `+0.10`).
- Confidence scores below a threshold (default: `0.3`) are flagged as **Contested** in the UI and in artifacts.
- The Orchestrator records all confidence transitions in the patch log for auditability.

### 3.3 Friction Level (0–100)

User-controlled **Friction Level** modulates both agent tone and convergence requirements:

| Range | Mode | Effect |
|---|---|---|
| 0–30 | Brainstorm / additive | "Yes-and" tone. Low convergence threshold. Critique is constructive and solution-oriented. |
| 31–70 | Balanced critique | Agents challenge claims but also propose alternatives. Standard convergence threshold. |
| 71–100 | Adversarial / red-team | Contrarian assumes the idea is wrong until proven otherwise. Convergence threshold rises — higher evidence quality and assumption coverage required before session terminates. |

> **Key design rule:** Friction must modulate *what counts as resolved*, not just tone. At friction 90, convergence requires evidence_quality ≥ 0.7 and assumption_explicitness ≥ 0.8, not just confident-sounding text.

### 3.4 Human-in-the-Loop (HITL) "Tap an Agent"

During debate the user can:
- **Pause** the current round
- **Challenge** a specific agent's claim (triggers a targeted defense response)
- **Force a deep dive** on a specific point (inserts a micro-round scoped to that claim)
- **Add or modify constraints** (triggers **Change Propagation** — see §3.5)
- **Redirect agents** to address an open question before advancing

### 3.5 Change Propagation Policy

When any constraint, assumption, or decision changes mid-session (via HITL or system update):

1. Orchestrator computes the **impact set**: all claims, assumptions, and risks with `derived_from` paths that include the changed entity.
2. Orchestrator schedules **targeted reevaluation tasks** for the affected agents only (not a full re-run).
3. Affected claims are temporarily marked **Pending Revalidation** in the UI.
4. Agents respond to targeted tasks within a bounded micro-round (max 1 round, max 1 response per affected agent).
5. Patches from micro-round are applied; convergence scores are recalculated.

### 3.6 Pause-and-Replay (Scenario Forking)

The patch-log architecture enables session forking:
- User selects any prior Truth Map snapshot from the session timeline.
- User modifies one or more constraints or assumptions at that snapshot.
- The system creates a **forked session** branched from that snapshot and reruns the debate forward from that point.
- Both the original and the forked session are preserved; the user can diff artifacts between them.

This turns Agon from a one-shot analysis tool into a genuine **scenario planning tool** — "what if we halved the budget?" or "what if the target user is enterprise, not consumer?" become first-class operations.

---

## 4) Agent Roster & Model Assignment

### 4.1 Models (v1)
- OpenAI **GPT-5.2 Thinking**
- Google **Gemini 3** (thinking_level: high)
- Anthropic **Claude Opus 4.6** (reasoning tier)
- DeepSeek **DeepSeek-V3.2** (thinking mode)

### 4.2 Model Usage Policy

Models are **not hardwired to roles by personality**. Instead:
- Each agent has a **primary model** for its core analytical task.
- The Orchestrator may route **low-stakes sub-tasks** (patch formatting, question generation, summarisation passes) to a cheaper/faster model.
- The Orchestrator escalates back to the primary reasoning model for analytical and evaluative work.
- All provider calls go through `IChatModelClient` — providers are interchangeable behind the interface.

### 4.3 Agent Roster

| # | Agent | Primary Model | Role |
|---|---|---|---|
| 1 | Orchestrator / Moderator | Deterministic controller + light LLM assist | Session state machine, patch validation, convergence scoring |
| 2 | Socratic Clarifier | GPT-5.2 Thinking | Extract intent, constraints, success metrics, produce Debate Brief |
| 3 | Framing Challenger | Gemini 3 | Attack the *framing* of the problem itself — challenges whether the user is solving the right problem |
| 4 | Product Strategist | Claude Opus 4.6 | User value, MVP scope, positioning, UX principles |
| 5 | Technical Architect | DeepSeek-V3.2 | Feasibility, architecture, cost hotspots, failure modes |
| 6 | Contrarian / Red Team | Gemini 3 | Logical fallacies, market risks, security, failure modes — attacks solutions |
| 7 | Research Librarian | Tools + GPT-5.2 summarisation | Optional — web/market research, evidence stored in Truth Map |
| 8 | Synthesis + Validation | GPT-5.2 Thinking | Unify into coherent plan, score via rubric, identify contradictions and missing validations in a single pass |

> **Note on v1 collapse:** The prior spec had separate Synthesizer and Critic/Editor agents running sequentially. These have been merged into a single **Synthesis + Validation** agent that synthesises *and* scores in one pass. This eliminates the token-expensive loop where the Critic demands changes, Synthesizer rewrites, Critic re-scores. If improvements are required, a bounded **targeted loop** is triggered (see §5.2), not a full re-synthesis.

> **New addition:** The **Framing Challenger** is a dedicated agent for attacking the *problem definition*, not just the solution. This is the most commonly missing role in idea analysis — agents often debate how to build the wrong thing rather than questioning whether it is the right thing to build.

---

## 5) Agent Graph & Bounded Debate Policy

### 5.1 High-level flow

```
Intake
  └─► Clarification Loop (max 2 rounds, max 3 questions each)
        └─► Round 1 — Divergence (agents analyse independently, in parallel)
              └─► Round 2 — Crossfire (agents must explicitly critique each other)
                    └─► Synthesis + Validation (unified narrative + rubric score)
                          ├─► [if convergence >= threshold] → Deliver
                          └─► [if gaps remain] → Targeted Loop (bounded)
                                └─► Deliver
                                      └─► Post-Delivery (conversational follow-up — user can question, challenge, deep dive, modify constraints)
```

### 5.2 Bounded loop defaults (configurable per session)

| Parameter | Default | Notes |
|---|---|---|
| `max_clarification_rounds` | 2 | Hard cap on clarification phase |
| `max_debate_rounds` | 2 | Divergence + Crossfire |
| `max_targeted_loops` | 2 | Post-synthesis gap resolution |
| `max_session_budget_tokens` | tier-based | Hard ceiling on total spend |
| `convergence_threshold` | 0.75 (standard) / 0.85 (friction ≥ 70) | Score required to terminate early |

Stop early if **overall convergence score** ≥ threshold. Do not run additional loops for their own sake.

### 5.3 Convergence rubric (0.0–1.0 each)

| Dimension | Description | Required for "resolved" |
|---|---|---|
| Clarity & specificity | Is the idea clearly defined? | ≥ 0.7 |
| Feasibility | Is it technically and financially feasible? | ≥ 0.7 |
| Risk coverage | Are major risk categories addressed? | ≥ 0.7 |
| Assumption explicitness | Are assumptions named and validated? | ≥ 0.7 (≥ 0.8 at friction ≥ 70) |
| Coherence | No unresolved contradictions between agents | ≥ 0.8 |
| Actionability | Concrete next steps defined | ≥ 0.7 |
| Evidence quality | Unverified claims are flagged (if research disabled, auto-capped at 0.6) | ≥ 0.5 (≥ 0.7 at friction ≥ 70) |

---

## 6) Modules & Requirements

### Module A — Intake & Socratic Clarification

**Goal:** Convert raw user input into a structured **Debate Brief** and seed the Truth Map.

**Requirements**
- Auto-detect ambiguity; trigger clarifier only when needed (avoid unnecessary friction for clear inputs).
- Stepper UI: 1–3 targeted clarifying questions in sequence, not all at once.
- Produce `DebateBrief` + initial Truth Map (constraints, success metrics, persona).
- Flag any constraints that are suspiciously vague (e.g., "unlimited budget") for later challenge.

---

### Module B — Council Chamber (Debate Engine)

**Goal:** Run structured, bounded debate through controlled rounds.

**Requirements**
- **Parallel execution** for Round 1 (Divergence): all agents analyse simultaneously.
- **Enforced cross-critique** for Round 2 (Crossfire): every agent must explicitly reference at least one claim from another agent.
- Every agent response MUST output two sections:
  - `MESSAGE` — human-readable Markdown analysis
  - `PATCH` — machine-readable `TruthMapPatch` JSON (see SCHEMAS.md)
- Orchestrator validates patches before applying (schema validation + conflict resolution).
- **Framing Challenger fires in Round 1** with a special directive: challenge the problem definition, not the proposed solution.

---

### Module C — Global Workspace + Context Vault

**Goal:** Persistent memory, real-time state synchronisation, and entity-linked graph.

**Requirements**
- Truth Map stored as:
  - **Relational source of truth** (JSONB + normalised tables with entity link tracking)
  - **Append-only patch event log** (full provenance: which agent changed what, when, and why)
  - **Vector memory** (pgvector) for semantic retrieval
- Context injection on each agent call: current Truth Map + top-K relevant memories.
- **Confidence decay engine**: runs after each round to update claim confidence based on challenge/defence activity and linked evidence.
- **Change impact calculator**: on constraint/assumption change, compute derived entity impact set for targeted reevaluation.
- **Snapshot service**: write an immutable Truth Map snapshot at the end of each round to support Pause-and-Replay forking.

---

### Module D — Debate Theatre UI (Living Strategy Room)

**Goal:** Make the debate understandable, steerable, and transparent.

**Core UI elements**

- **Thread View (mobile-first):** premium group-chat aesthetic with structured agent cards. Contested claims (confidence < 0.3) rendered with a visual warning indicator.
- **Truth Map Drawer:** always accessible — bottom sheet (mobile) / right panel (desktop). Shows the live graph with entity links visible on hover.
- **Friction Slider:** visible in session header. Tooltip explains both tone and convergence threshold effects.
- **Tap an Agent:** contextual action on any message — "ask why", "challenge", "expand", "deep dive".
- **Round Timeline:** Clarify → Round 1 → Round 2 → Synthesise + Validate → Deliver. Progress indicator always visible.
- **Change Propagation Indicator:** when a mid-session constraint change triggers reevaluation, affected claims are highlighted as "Pending Revalidation" until resolved.
- **Session Timeline Scrubber (Phase 1.5):** access prior snapshots and initiate Pause-and-Replay forks.
- **Map View (desktop, Phase 1.5):** agent nodes + challenge/support edges visualising the claim graph.

---

### Module E — Output Pack (Artifacts)

**Goal:** Produce a developer-usable, exec-readable set of outputs.

**Artifacts**

| File | Contents |
|---|---|
| `Verdict.md` | Go/No-Go + rationale + conditions for "Yes" + contested claims summary |
| `Plan.md` | Phased plan: MVP / v1 / v2, 30-60-90 day breakdown |
| `PRD.md` | The user's idea formalised as a complete PRD |
| `Risk-Registry.md` | All risks with severity, likelihood, mitigation, and source agent |
| `Assumption-Validation.md` | Every assumption with its validation step and current confidence |
| `.github/copilot-instructions.md` | Repo-specific development rules |
| `Architecture.mmd` | Mermaid diagram text for proposed system architecture |
| `Scenario-Diff.md` | (If Pause-and-Replay was used) — diff of key decisions between the original and forked scenarios |

---

## 7) Non-Functional Requirements

- **Streaming:** time-to-first-token per agent must feel instant. UI must never appear "stuck" — show streaming progress indicators throughout.
- **Cost controls:** hard budget ceiling per session tier. If budget is near exhaustion, degrade gracefully: reduce agent count (drop Research Librarian first), then reduce loop count. Never silently truncate — surface budget status to the user.
- **Security:** encrypt stored idea content at rest. "No persistence" mode available per session (nothing written to DB, ephemeral only).
- **Observability:** per-agent latency, per-provider cost, convergence score history, and patch operation counts tracked per session.
- **Determinism:** Orchestrator state transitions MUST be deterministic. LLM calls must never decide policy transitions.

---

## 8) Future (Phase 2)

- SwiftUI iOS app using the same backend API.
- Team workspaces and shared idea rooms with collaborative HITL.
- Attachments + RAG over user documents.
- "Simulation mode": run the plan through synthetic timelines and resource constraints.
- Continuous mode: subscribe to a live idea (e.g., a product strategy) and re-run the council when external signals change (market news, competitor releases).
