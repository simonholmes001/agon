"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import SessionHeader from "@/components/session/session-header";
import ThreadView, { type ThreadViewMessage } from "@/components/session/thread-view";
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
const BACKEND_API_PREFIX = "/api/backend";

interface BackendSessionResponse {
  sessionId?: string;
  phase?: string;
  frictionLevel?: number;
}

interface BackendTruthMapResponse {
  coreIdea?: string;
}

interface BackendTranscriptMessageResponse {
  id?: string;
  type?: string;
  agentId?: string;
  content?: string;
  round?: number;
  isStreaming?: boolean;
}

async function readJsonResponse<T>(
  response: Response,
  requestName: string,
): Promise<T> {
  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.toLowerCase().includes("application/json")) {
    const bodyPreview = (await response.text()).slice(0, 120);
    throw new Error(
      `${requestName} returned non-JSON response (${contentType || "unknown"}). Preview: ${bodyPreview}`,
    );
  }

  return await response.json() as T;
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

function mapTranscriptMessages(
  transcript: BackendTranscriptMessageResponse[],
): ThreadViewMessage[] {
  return transcript.flatMap((message, index) => {
    const content = message.content?.trim();
    if (!content) return [];

    const type = message.type?.toLowerCase() === "agent"
      ? "agent"
      : "system";

    return [{
      id: message.id ?? `transcript-${index}`,
      type,
      content,
      agentId: message.agentId,
      round: typeof message.round === "number" ? message.round : undefined,
      isStreaming: Boolean(message.isStreaming),
    }];
  });
}

export default function SessionPage() {
  const params = useParams<{ id?: string | string[] }>();
  const routeSessionId = getRouteSessionId(params.id);

  const [truthMapOpen, setTruthMapOpen] = useState(false);
  const [sessionId, setSessionId] = useState(routeSessionId ?? "");
  const [phase, setPhase] = useState<SessionPhase>("CLARIFICATION");
  const [frictionLevel, setFrictionLevel] = useState(50);
  const [coreIdea, setCoreIdea] = useState("");
  const [messages, setMessages] = useState<ThreadViewMessage[]>([]);
  const [realtimeStatus, setRealtimeStatus] = useState<"connecting" | "connected" | "unavailable">("connecting");

  const loadSessionState = useCallback(async (id: string) => {
    logger.info("loading session state", { sessionId: id });
    try {
      const [sessionResponse, truthMapResponse, transcriptResponse] = await Promise.all([
        fetch(`${BACKEND_API_PREFIX}/sessions/${id}`),
        fetch(`${BACKEND_API_PREFIX}/sessions/${id}/truthmap`),
        fetch(`${BACKEND_API_PREFIX}/sessions/${id}/transcript`),
      ]);

      if (!sessionResponse.ok) {
        throw new Error(`Get session failed with status ${sessionResponse.status}`);
      }

      const session = await readJsonResponse<BackendSessionResponse>(
        sessionResponse,
        "Get session",
      );

      if (!truthMapResponse.ok) {
        throw new Error(`Get truth map failed with status ${truthMapResponse.status}`);
      }

      const truthMap = await readJsonResponse<BackendTruthMapResponse>(
        truthMapResponse,
        "Get truth map",
      );

      if (!transcriptResponse.ok) {
        throw new Error(`Get transcript failed with status ${transcriptResponse.status}`);
      }

      const transcript = await readJsonResponse<BackendTranscriptMessageResponse[]>(
        transcriptResponse,
        "Get transcript",
      );

      setSessionId(session.sessionId ?? id);
      setPhase(mapBackendPhaseToSessionPhase(session.phase));
      setCoreIdea(truthMap.coreIdea ?? "");
      setMessages(mapTranscriptMessages(transcript));
      if (typeof session.frictionLevel === "number") {
        setFrictionLevel(session.frictionLevel);
      }

      logger.info("loaded session state", {
        sessionId: session.sessionId ?? id,
        phase: session.phase,
        frictionLevel: session.frictionLevel,
        transcriptMessageCount: transcript.length,
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

    setRealtimeStatus("connecting");
    const connection = createDebateHubConnection(routeSessionId);
    connection.onRoundProgress((event: RoundProgressEvent) => {
      setPhase(mapBackendPhaseToSessionPhase(event.phase));
      setRealtimeStatus("connected");
      logger.info("received round progress event", {
        sessionId: routeSessionId,
        phase: event.phase,
      });
    });

    connection.onTruthMapPatch((event: TruthMapPatchEvent) => {
      setRealtimeStatus("connected");
      logger.info("received truth map patch event", {
        sessionId: routeSessionId,
        version: event.version,
      });
    });

    connection.onReconnected(() => {
      setRealtimeStatus("connected");
      logger.warn("signalr reconnect detected, resyncing session state", {
        sessionId: routeSessionId,
      });
      void loadSessionState(routeSessionId);
    });

    void connection
      .start()
      .then(() => {
        setRealtimeStatus("connected");
      })
      .catch((error) => {
        setRealtimeStatus("unavailable");
        logger.warn(
          "signalr unavailable, continuing with rest-only session state",
          { sessionId: routeSessionId },
          error,
        );
      });

    return () => {
      void connection.stop().catch((error) => {
        logger.warn(
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
        <ThreadView
          sessionId={sessionId || routeSessionId || "unknown"}
          phase={phase}
          coreIdea={coreIdea}
          realtimeStatus={realtimeStatus}
          messages={messages}
        />

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
