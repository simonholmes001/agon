---
applyTo: '**/*.{cs,ts}'
---
# Agon Schemas (v2.0)

---

## 1) DebateBrief

Output of the Socratic Clarifier. Seeds the initial Truth Map.

```json
{
  "core_idea": "string — one sentence description",
  "constraints": {
    "budget": "string",
    "timeline": "string",
    "tech_stack": ["string"],
    "non_negotiables": ["string"]
  },
  "success_metrics": ["string"],
  "primary_persona": "string",
  "open_questions": ["string"]
}
```

---

## 2) TruthMap

The authoritative session state. Always the source of truth — never the transcript.

```json
{
  "session_id": "uuid",
  "version": "integer — increments on every applied patch",
  "round": "integer — current debate round",

  "core_idea": "string",

  "constraints": {
    "budget": "string",
    "timeline": "string",
    "tech_stack": ["string"],
    "non_negotiables": ["string"]
  },

  "success_metrics": ["string"],

  "personas": [
    { "id": "string", "name": "string", "description": "string" }
  ],

  "claims": [
    {
      "id": "string",
      "agent": "string — agent_id that authored this claim",
      "round": "integer — round in which this claim was made",
      "text": "string",
      "confidence": "float 0.0–1.0",
      "status": "active | contested | pending_revalidation",
      "derived_from": ["string — entity_id"],
      "challenged_by": ["string — entity_id of the challenging claim or risk"]
    }
  ],

  "assumptions": [
    {
      "id": "string",
      "text": "string",
      "validation_step": "string — how to test this assumption",
      "derived_from": ["string — entity_id"],
      "status": "unvalidated | validated | invalidated"
    }
  ],

  "decisions": [
    {
      "id": "string",
      "text": "string",
      "rationale": "string",
      "owner": "string — agent_id or 'orchestrator'",
      "derived_from": ["string — entity_id"],
      "binding": "boolean"
    }
  ],

  "risks": [
    {
      "id": "string",
      "text": "string",
      "category": "market | technical | execution | security | financial",
      "severity": "low | medium | high | critical",
      "likelihood": "low | medium | high",
      "mitigation": "string",
      "derived_from": ["string — entity_id"],
      "agent": "string — agent_id that identified this risk"
    }
  ],

  "open_questions": [
    {
      "id": "string",
      "text": "string",
      "blocking": "boolean — if true, must be resolved before session can converge",
      "raised_by": "string — agent_id"
    }
  ],

  "evidence": [
    {
      "id": "string",
      "title": "string",
      "source": "string — URL or citation",
      "retrieved_at": "ISO-8601 timestamp",
      "summary": "string — brief summary of the evidence",
      "supports": ["string — entity_id of claims or assumptions this evidence supports"],
      "contradicts": ["string — entity_id of claims or assumptions this evidence challenges"]
    }
  ],

  "convergence": {
    "clarity_specificity": "float 0.0–1.0",
    "feasibility": "float 0.0–1.0",
    "risk_coverage": "float 0.0–1.0",
    "assumption_explicitness": "float 0.0–1.0",
    "coherence": "float 0.0–1.0",
    "actionability": "float 0.0–1.0",
    "evidence_quality": "float 0.0–1.0",
    "overall": "float 0.0–1.0 — weighted average",
    "threshold": "float — the required score to converge (friction-adjusted)",
    "status": "in_progress | converged | gaps_remain"
  },

  "confidence_transitions": [
    {
      "claim_id": "string",
      "round": "integer",
      "from": "float",
      "to": "float",
      "reason": "challenged_no_defense | evidence_corroboration | manual_override"
    }
  ]
}
```

---

## 3) TruthMapPatch

The only way agents change the Truth Map. Every patch is validated by the Orchestrator before being applied.

```json
{
  "ops": [
    {
      "op": "add | replace | remove",
      "path": "/risks/-",
      "value": {
        "id": "r1",
        "text": "string",
        "category": "market",
        "severity": "high",
        "likelihood": "medium",
        "mitigation": "string",
        "derived_from": ["claim-id-123"],
        "agent": "contrarian"
      }
    },
    {
      "op": "replace",
      "path": "/claims/0/status",
      "value": "contested"
    },
    {
      "op": "add",
      "path": "/claims/0/challenged_by/-",
      "value": "risk-id-r1"
    }
  ],
  "meta": {
    "agent": "string — agent_id proposing this patch",
    "round": "integer",
    "reason": "string — human-readable explanation of why this patch is proposed",
    "session_id": "uuid"
  }
}
```

### Patch validation rules

The Orchestrator rejects patches that:
- Reference entity IDs that do not exist in the current Truth Map (unless `op` is `add`).
- Use `replace` or `remove` on an entity whose `id` does not match the target.
- Modify `text` of a claim authored by a different agent (agents may update their own claims; they may add `challenged_by` references to others' claims but must not overwrite others' text).
- Propose a `decision` without a `rationale` field.
- Add an `assumption` without a `validation_step` field (null is not acceptable after Round 2).

---

## 4) ConfidenceDecayConfig

Configurable per session (defaults shown).

```json
{
  "decay_step": 0.15,
  "boost_step": 0.10,
  "contested_threshold": 0.30,
  "decay_trigger": "challenged_in_round_no_defense",
  "boost_trigger": "evidence_linked_with_supports"
}
```

---

## 5) RoundPolicy

The Orchestrator's session configuration. Can be overridden per session tier.

```json
{
  "max_clarification_rounds": 2,
  "max_debate_rounds": 2,
  "max_targeted_loops": 2,
  "max_session_budget_tokens": 200000,
  "convergence_threshold_standard": 0.75,
  "convergence_threshold_high_friction": 0.85,
  "high_friction_cutoff": 70,
  "confidence_decay": {
    "decay_step": 0.15,
    "boost_step": 0.10,
    "contested_threshold": 0.30
  }
}
```

---

## 6) SessionSnapshot

Written at the end of every round. Immutable once created.

```json
{
  "snapshot_id": "uuid",
  "session_id": "uuid",
  "round": "integer",
  "created_at": "ISO-8601",
  "truth_map_hash": "string — SHA-256 of the truth_map JSON at this point",
  "truth_map": { "— full TruthMap object as above —" }
}
```

---

## 7) ForkRequest

Used to initiate a Pause-and-Replay branch.

```json
{
  "parent_session_id": "uuid",
  "snapshot_id": "uuid — which round snapshot to branch from",
  "initial_patches": [
    {
      "— TruthMapPatch ops to apply before the forked debate resumes —"
    }
  ],
  "label": "string — human-readable description of this scenario (e.g., 'What if budget is halved?')"
}
```
