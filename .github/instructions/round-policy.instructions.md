---
applyTo: '**'
---
# Agon Round Policy Specification

**Version:** 1.0  
**Purpose:** This is the "constitution of the council" — the complete specification of session state transitions, round boundaries, timeout behaviour, targeted loop triggers, and termination conditions. Every Orchestrator implementation decision must be consistent with this document.

---

## 1) Session Phases

An Agon session passes through these phases in order. Phases are strictly sequential. The Orchestrator cannot skip forward and cannot return to a prior phase except via the defined re-entry conditions.

```
INTAKE
  └─► CLARIFICATION
        └─► DEBATE_ROUND_1
              └─► DEBATE_ROUND_2
                    └─► SYNTHESIS
                          ├─► CONVERGED → DELIVER → POST_DELIVERY
                          └─► TARGETED_LOOP (up to N times)
                                ├─► SYNTHESIS (re-entry)
                                └─► MAX_LOOPS_REACHED → DELIVER_WITH_GAPS → POST_DELIVERY
```

---

## 2) Phase Definitions and Transition Rules

### 2.1 INTAKE

**Entry condition:** User submits an idea (minimum 10 characters of non-whitespace text).  
**What happens:** Orchestrator creates session, seeds empty Truth Map, initialises RoundPolicy from tier config.  
**Exit condition:** Automatically transitions to CLARIFICATION.  
**Failure condition:** None in this phase.

---

### 2.2 CLARIFICATION

**Entry condition:** Arrived from INTAKE.  
**Agents active:** Socratic Clarifier only.  
**Max rounds in this phase:** `max_clarification_rounds` (default: 2).  
**Max questions per round:** 3.

**Within-phase loop:**
1. Socratic Clarifier evaluates the current Truth Map.
2. If all three elements of the Golden Triangle are present and unambiguous: Clarifier outputs `READY` and the phase ends.
3. If elements are missing or vague: Clarifier outputs up to 3 questions. Questions are presented to the user. User responds.
4. Clarifier processes the response, updates Truth Map via patch, re-evaluates.
5. Repeat up to `max_clarification_rounds` times.

**Forced exit:** If `max_clarification_rounds` is reached without a `READY` signal, Orchestrator transitions to DEBATE_ROUND_1 anyway with the best available Debate Brief. Flags the session as `clarification_incomplete = true`.

**HITL during clarification:** User may add constraints directly. Treated as a clarification response. Does not consume a clarification round.

**Exit condition:** Socratic Clarifier outputs `READY` → transition to DEBATE_ROUND_1.

---

### 2.3 DEBATE_ROUND_1 (Divergence)

**Entry condition:** Arrived from CLARIFICATION with a seeded Truth Map.  
**Agents active:** Framing Challenger, Product Strategist, Technical Architect, Contrarian, Research Librarian (if enabled). All run in **parallel**.  
**Timeout per agent:** Configurable. Default: 90 seconds wall-clock from first token request to complete response. If an agent times out, its slot is skipped for this round. Session continues without it.

**Execution sequence:**
1. Orchestrator dispatches all Round 1 agent calls simultaneously (parallelism bounded by provider rate limits).
2. Each agent streams its `MESSAGE` to the UI in real time.
3. Each agent's `PATCH` is buffered until the full response is received.
4. Patches are validated and applied sequentially in a deterministic order (alphabetical by agent_id to avoid ordering ambiguity).
5. After all patches are applied, Confidence Decay Engine runs for the round.

**Post-round actions:**
1. Orchestrator writes round-end snapshot.
2. Orchestrator broadcasts updated convergence scores to UI.
3. Orchestrator checks for contested claims (confidence < contested_threshold). If any exist, broadcasts Contested signals to UI.

**Exit condition:** All active agents have responded (or timed out) → transition to DEBATE_ROUND_2.

---

### 2.4 DEBATE_ROUND_2 (Crossfire)

**Entry condition:** Arrived from DEBATE_ROUND_1.  
**Agents active:** Framing Challenger (optional — only if its framing challenge was not addressed), Product Strategist, Technical Architect, Contrarian. **Sequential or parallel**, depending on implementation preference. (Sequential is simpler and avoids agents cross-referencing claims that haven't been patched yet. Parallel with a snapshot of Round 1 Truth Map is also valid but requires careful patch ordering.)  
**Crossfire requirement:** Every active agent MUST reference at least one claim from another agent by entity ID. Orchestrator validates this in the patch. If an agent's Round 2 response contains no `challenged_by` references on any claim and no explicit agent reference in the MESSAGE, the response is flagged as `crossfire_incomplete` but is not rejected.

**HITL during Round 2:**
- User may "Tap an Agent" at any point to challenge a specific claim.
- Tapping inserts a **micro-directive** into that agent's next call context: "The user is specifically challenging [claim_id]. Address this before continuing your analysis."
- This does not interrupt the current round. It is injected at the next agent call opportunity.

**Post-round actions:** Same as Round 1 (snapshot, convergence broadcast, contested signals).

**Exit condition:** All active agents have responded (or timed out) → transition to SYNTHESIS.

---

### 2.5 SYNTHESIS

**Entry condition:** Arrived from DEBATE_ROUND_2 or from TARGETED_LOOP (re-entry).  
**Agent active:** Synthesis + Validation (single agent, sequential).  
**Input:** Full Truth Map + all agent MESSAGE summaries from all prior rounds.

**Execution:**
1. Agent runs synthesis (executive summary, decisions, plan, PRD outline).
2. Agent runs validation rubric scoring in the same pass.
3. Agent outputs `CONVERGED` or `GAPS_REMAIN` with specific gap details.
4. Patch is applied: convergence scores updated, decisions written, assumptions validated.

**Convergence check (Orchestrator):**
- Read `convergence.overall` from Truth Map after patch is applied.
- Compare against `convergence_threshold` (friction-adjusted).
- Check for any `open_questions` with `blocking: true`.
- If `overall >= threshold` AND no blocking open questions: transition to DELIVER.
- Otherwise: check if `targeted_loop_count < max_targeted_loops`. If yes: transition to TARGETED_LOOP.
- If `targeted_loop_count >= max_targeted_loops`: transition to DELIVER_WITH_GAPS.

---

### 2.6 TARGETED_LOOP

**Entry condition:** SYNTHESIS output was `GAPS_REMAIN` and loop budget has not been exhausted.  
**Purpose:** Address specific identified gaps without re-running the full debate.  
**Loop counter:** Incremented each time this phase is entered. Hard ceiling: `max_targeted_loops`.

**Execution:**
1. Orchestrator reads the gap specification from the Synthesis + Validation agent's patch.
2. Orchestrator identifies which agents are responsible for the gap dimensions (e.g., low risk_coverage → dispatch Contrarian; low feasibility → dispatch Technical Architect).
3. Orchestrator dispatches targeted calls to those agents only, with the gap directive injected into context.
4. Agents respond with focused patches addressing only the identified gaps.
5. Patches applied. Confidence Decay Engine runs.
6. Round-end snapshot written.
7. Re-enter SYNTHESIS.

**Preventing agent over-correction:** In a targeted loop, agents are explicitly instructed not to introduce new major claims or expand scope. Their task is narrowly scoped to the identified gaps.

---

### 2.7 DELIVER

**Entry condition:** Convergence threshold met and no blocking open questions.  
**Actions:**
1. Orchestrator triggers artifact generation for all output types (Verdict, Plan, PRD, Risk Registry, Assumption Validation, Architecture Mermaid, Copilot Instructions).
2. Artifacts are generated from the Truth Map (not from transcript).
3. Session status set to `complete`.
4. User is notified and presented with the artifact pack.
5. Transition to POST_DELIVERY.

---

### 2.8 DELIVER_WITH_GAPS

**Entry condition:** Max targeted loops exhausted before convergence threshold was met.  
**Actions:**
1. Same artifact generation as DELIVER.
2. Verdict artifact includes a prominent **"Unresolved Gaps"** section listing all dimensions that did not meet threshold and the specific open questions that remain.
3. Session status set to `complete_with_gaps`.
4. User is presented with the artifact pack with a clear notice that gaps remain.
5. Transition to POST_DELIVERY.

---

### 2.9 POST_DELIVERY (Conversational Follow-Up)

**Entry condition:** Arrived from DELIVER or DELIVER_WITH_GAPS. Session status is `complete` or `complete_with_gaps`.  
**Purpose:** Allow the user to continue asking questions, challenging claims, and exploring the output after artifacts have been generated. The session remains a living workspace — delivery is not the end of the conversation.

**Agents available:** All agents from the council remain available on-demand. They are not called proactively — they respond only to user-initiated actions.

**User capabilities in POST_DELIVERY:**
- **Ask questions** about any aspect of the output, Truth Map, or debate history. Routed to the most relevant agent based on the question topic (e.g., technical questions → Technical Architect, risk questions → Contrarian).
- **Challenge a claim** in the delivered artifacts. Triggers a targeted response from the authoring agent + the Contrarian for balance.
- **Request a deep dive** on a specific decision, risk, or assumption. Dispatches a micro-round scoped to that entity.
- **Modify a constraint** (triggers Change Propagation as defined in §3, followed by artifact regeneration for affected sections).
- **Request artifact regeneration** after post-delivery changes have accumulated.

**Execution rules:**
1. Each user action dispatches a single targeted agent call (or a bounded micro-round for deep dives / constraint changes).
2. Truth Map continues to be updated via patches. All post-delivery patches carry `round: post_delivery` in their provenance.
3. Confidence Decay Engine runs after each post-delivery interaction that produces patches.
4. If a constraint change triggers Change Propagation, affected artifacts are marked as `stale` in the UI until the user requests regeneration.
5. Post-delivery interactions consume from the session token budget. Budget warnings and degradation policy (§4) continue to apply.

**Memory and context:**
- The full Truth Map + conversation history remain in context for all post-delivery agent calls.
- Semantic memory (pgvector) supports natural-language queries: "what did we decide about pricing?", "why was the monolith approach rejected?", "show all contested claims".

**Session status transitions:**
- Session status remains `complete` or `complete_with_gaps` during post-delivery.
- If the user explicitly closes the session, status transitions to `closed`. No further interactions are accepted.
- Post-delivery has no round limit, but is bounded by the session token budget.

---

## 3) HITL Mid-Session Constraint Change (Change Propagation)

This is the most complex transition in the system. It can occur during any active debate phase.

**Trigger:** User adds or modifies a constraint via the HITL interface.

**Sequence:**
1. Constraint is immediately applied to Truth Map as a patch (agent: `user`, round: current_round).
2. Orchestrator calls `ChangeImpactCalculator.GetImpactSet(constraint_id)`.
3. All entities in the impact set are marked `pending_revalidation` in Truth Map.
4. Orchestrator broadcasts `PendingRevalidation` events to UI for affected claim IDs.
5. **Orchestrator does NOT interrupt the current active agent call.** It buffers the reevaluation tasks.
6. After the current round completes normally, Orchestrator runs a micro-round:
   - Only the agents responsible for the affected entities are called.
   - Each agent receives the updated Truth Map + a directive: "The following constraint has changed: [constraint]. Re-evaluate your prior claims [claim_ids] in light of this change."
   - Max one response per affected agent. No crossfire in a micro-round.
7. Patches from micro-round are applied.
8. Entities that were `pending_revalidation` are updated to `active` (or `contested` if confidence dropped).
9. Convergence scores are recalculated.
10. Session resumes at the phase it was in before the change.

**Budget impact:** Micro-rounds consume from the session token budget. If the budget is near exhaustion when a constraint change occurs, the Orchestrator surfaces a warning to the user: "Reevaluation requires approximately X tokens. Your session budget is at Y%. Proceed?"

---

## 4) Timeout and Degradation Policy

| Condition | Response |
|---|---|
| Single agent timeout (> 90s) | Skip agent for this round. Log timeout. Session continues. |
| Multiple agent timeouts in same round | Surface warning to user: "Some agents did not respond in time." Session continues with available responses. |
| Provider API error (5xx) | Retry once after 5s. If retry fails, skip agent for this round. Do not surface raw error to user — show "Agent temporarily unavailable." |
| Budget at 80% | Surface passive warning to user in session header. |
| Budget at 95% | Surface prominent warning. Offer user the option to: (a) end session and deliver with current state, or (b) continue with reduced agent set (Research Librarian dropped first, then Framing Challenger). |
| Budget exhausted | Immediately stop all agent calls. Transition to DELIVER_WITH_GAPS. Generate artifacts from current Truth Map state. |

---

## 5) Pause-and-Replay Transition

**Trigger:** User initiates a fork from the Session Timeline Scrubber (Phase 1.5 UI feature).

**Sequence:**
1. User selects a round snapshot.
2. User specifies changes (constraint modifications, assumption overrides).
3. System calls `SnapshotService.ForkSession(session_id, snapshot_id, initial_patches)`.
4. A new session is created with:
   - `forked_from = original_session_id`
   - `fork_snapshot_id = selected_snapshot_id`
   - Truth Map initialised from the snapshot with the user's initial patches applied.
5. The forked session enters DEBATE_ROUND_1 (or whichever phase the snapshot was taken at, if mid-debate — re-enter at the phase that follows the snapshot round).
6. Both sessions are independent from this point forward.
7. When the forked session reaches DELIVER, a `Scenario-Diff.md` artifact is generated comparing key decisions between the original and the fork.

---

## 6) Convergence Threshold Quick Reference

| friction_level | overall_threshold | assumption_explicitness min | evidence_quality min |
|---|---|---|---|
| 0–30 | 0.65 | 0.60 | 0.40 |
| 31–70 | 0.75 | 0.70 | 0.50 |
| 71–100 | 0.85 | 0.80 | 0.70 |
