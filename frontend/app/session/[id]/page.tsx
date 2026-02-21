"use client";

import { useEffect, useState } from "react";
import { useParams } from "next/navigation";
import SessionHeader from "@/components/session/session-header";
import ThreadView from "@/components/session/thread-view";
import TruthMapDrawer from "@/components/session/truth-map-drawer";
import MessageComposer from "@/components/session/message-composer";
import { createLogger } from "@/lib/logger";
import type { SessionPhase } from "@/types";

const logger = createLogger("SessionPage");

interface BackendSessionResponse {
  sessionId?: string;
  phase?: string;
  frictionLevel?: number;
}

function mapBackendPhaseToSessionPhase(phase: string | undefined): SessionPhase {
  switch (phase) {
    case "Intake":
    case "INTAKE":
      return "INTAKE";
    case "Clarification":
    case "CLARIFICATION":
      return "CLARIFICATION";
    case "DebateRound1":
    case "DEBATE_ROUND_1":
      return "DEBATE_ROUND_1";
    case "DebateRound2":
    case "DEBATE_ROUND_2":
      return "DEBATE_ROUND_2";
    case "Synthesis":
    case "SYNTHESIS":
      return "SYNTHESIS";
    case "TargetedLoop":
    case "TARGETED_LOOP":
      return "TARGETED_LOOP";
    case "Deliver":
    case "DELIVER":
      return "DELIVER";
    case "DeliverWithGaps":
    case "DELIVER_WITH_GAPS":
      return "DELIVER_WITH_GAPS";
    case "PostDelivery":
    case "POST_DELIVERY":
      return "POST_DELIVERY";
    default:
      return "CLARIFICATION";
  }
}

function getRouteSessionId(id: string | string[] | undefined): string | undefined {
  if (Array.isArray(id)) {
    return id[0];
  }
  return id;
}

export default function SessionPage() {
  const params = useParams<{ id?: string | string[] }>();
  const routeSessionId = getRouteSessionId(params.id);

  const [truthMapOpen, setTruthMapOpen] = useState(false);
  const [sessionId, setSessionId] = useState(routeSessionId ?? "");
  const [phase, setPhase] = useState<SessionPhase>("CLARIFICATION");
  const [frictionLevel, setFrictionLevel] = useState(50);

  useEffect(() => {
    if (!routeSessionId) return;

    let isCancelled = false;

    async function loadSession() {
      try {
        const sessionResponse = await fetch(`/sessions/${routeSessionId}`);
        if (!sessionResponse.ok) {
          throw new Error(`Get session failed with status ${sessionResponse.status}`);
        }

        const session = await sessionResponse.json() as BackendSessionResponse;

        const truthMapResponse = await fetch(`/sessions/${routeSessionId}/truthmap`);
        if (!truthMapResponse.ok) {
          throw new Error(`Get truth map failed with status ${truthMapResponse.status}`);
        }

        await truthMapResponse.json();

        if (isCancelled) return;

        setSessionId(session.sessionId ?? routeSessionId);
        setPhase(mapBackendPhaseToSessionPhase(session.phase));
        if (typeof session.frictionLevel === "number") {
          setFrictionLevel(session.frictionLevel);
        }
      } catch (error) {
        logger.error("failed to load session page data", { sessionId: routeSessionId }, error);
      }
    }

    void loadSession();

    return () => {
      isCancelled = true;
    };
  }, [routeSessionId]);

  return (
    <div className="flex h-[100dvh] flex-col bg-background">
      <SessionHeader
        sessionId={sessionId || routeSessionId || "unknown"}
        phase={phase}
        frictionLevel={frictionLevel}
        onFrictionChange={setFrictionLevel}
        onToggleTruthMap={() => setTruthMapOpen(!truthMapOpen)}
        truthMapOpen={truthMapOpen}
      />

      <div className="relative flex flex-1 overflow-hidden">
        {/* Thread — always visible */}
        <ThreadView sessionId={sessionId || routeSessionId || "unknown"} />

        {/* Truth Map — drawer on mobile, sidebar on desktop */}
        <TruthMapDrawer
          open={truthMapOpen}
          onOpenChange={setTruthMapOpen}
        />
      </div>

      <MessageComposer
        sessionId={sessionId || routeSessionId || "unknown"}
        phase={phase}
      />
    </div>
  );
}
