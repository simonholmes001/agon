"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import SessionHeader from "@/components/session/session-header";
import ThreadView from "@/components/session/thread-view";
import TruthMapDrawer from "@/components/session/truth-map-drawer";
import MessageComposer from "@/components/session/message-composer";
import { createLogger } from "@/lib/logger";
import {
  createDebateHubConnection,
  type RoundProgressEvent,
  type TruthMapPatchEvent,
} from "@/lib/realtime/debate-hub";
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

  const loadSessionState = useCallback(async (id: string) => {
    logger.info("loading session state", { sessionId: id });
    try {
      const sessionResponse = await fetch(`/sessions/${id}`);
      if (!sessionResponse.ok) {
        throw new Error(`Get session failed with status ${sessionResponse.status}`);
      }

      const session = await sessionResponse.json() as BackendSessionResponse;

      const truthMapResponse = await fetch(`/sessions/${id}/truthmap`);
      if (!truthMapResponse.ok) {
        throw new Error(`Get truth map failed with status ${truthMapResponse.status}`);
      }

      await truthMapResponse.json();

      setSessionId(session.sessionId ?? id);
      setPhase(mapBackendPhaseToSessionPhase(session.phase));
      if (typeof session.frictionLevel === "number") {
        setFrictionLevel(session.frictionLevel);
      }

      logger.info("loaded session state", {
        sessionId: session.sessionId ?? id,
        phase: session.phase,
        frictionLevel: session.frictionLevel,
      });
    } catch (error) {
      logger.error("failed to load session page data", { sessionId: id }, error);
    }
  }, []);

  useEffect(() => {
    if (!routeSessionId) return;
    void loadSessionState(routeSessionId);
  }, [routeSessionId, loadSessionState]);

  useEffect(() => {
    if (!routeSessionId) return;

    const connection = createDebateHubConnection(routeSessionId);
    connection.onRoundProgress((event: RoundProgressEvent) => {
      setPhase(mapBackendPhaseToSessionPhase(event.phase));
      logger.info("received round progress event", {
        sessionId: routeSessionId,
        phase: event.phase,
      });
    });

    connection.onTruthMapPatch((event: TruthMapPatchEvent) => {
      logger.info("received truth map patch event", {
        sessionId: routeSessionId,
        version: event.version,
      });
    });

    connection.onReconnected(() => {
      logger.warn("signalr reconnect detected, resyncing session state", {
        sessionId: routeSessionId,
      });
      void loadSessionState(routeSessionId);
    });

    void connection.start().catch((error) => {
      logger.error(
        "failed to establish signalr connection",
        { sessionId: routeSessionId },
        error,
      );
    });

    return () => {
      void connection.stop().catch((error) => {
        logger.error(
          "failed to stop signalr connection",
          { sessionId: routeSessionId },
          error,
        );
      });
    };
  }, [routeSessionId, loadSessionState]);

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
