"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useParams, useSearchParams } from "next/navigation";
import SessionHeader from "@/components/session/session-header";
import ThreadView, { type ThreadViewMessage } from "@/components/session/thread-view";
import TruthMapDrawer from "@/components/session/truth-map-drawer";
import MessageComposer from "@/components/session/message-composer";
import { createLogger } from "@/lib/logger";
import {
  createDebateHubConnection,
  type RoundProgressEvent,
  type TranscriptMessageEvent,
  type TruthMapPatchEvent,
} from "@/lib/realtime/debate-hub";
import type { SessionPhase } from "@/types";

const logger = createLogger("SessionPage");
const BACKEND_API_PREFIX = "/api/backend";
const MODERATOR_AGENT_ID = "synthesis-validation";

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

interface BackendSessionMessageResponse {
  sessionId?: string;
  phase?: string;
  routedAgentId?: string;
  reply?: string;
  patchApplied?: boolean;
}

type RoundStartState = "idle" | "starting" | "started" | "failed";

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

function normalizeAgentId(agentId: string | undefined): string | undefined {
  return agentId?.trim().toLowerCase().replace(/_/g, "-");
}

function mapTranscriptMessages(
  transcript: BackendTranscriptMessageResponse[],
): ThreadViewMessage[] {
  return transcript
    .map((message, index) => mapTranscriptMessage(message, `transcript-${index}`))
    .filter((message): message is ThreadViewMessage => message !== null);
}

function mapTranscriptMessage(
  message: Pick<BackendTranscriptMessageResponse, "id" | "type" | "agentId" | "content" | "round" | "isStreaming">,
  fallbackId: string,
): ThreadViewMessage | null {
  const content = message.content?.trim();
  if (!content) return null;

  const normalizedType = message.type?.toLowerCase();
  const type = normalizedType === "agent"
    ? "agent"
    : normalizedType === "user"
      ? "user"
      : "system";
  return {
    id: message.id ?? fallbackId,
    type,
    content,
    agentId: message.agentId,
    round: typeof message.round === "number" ? message.round : undefined,
    isStreaming: Boolean(message.isStreaming),
  };
}

function upsertTranscriptMessage(
  messages: ThreadViewMessage[],
  nextMessage: ThreadViewMessage,
): ThreadViewMessage[] {
  const existingIndex = messages.findIndex((message) => message.id === nextMessage.id);
  if (existingIndex < 0) {
    return [...messages, nextMessage];
  }

  const updated = [...messages];
  updated[existingIndex] = nextMessage;
  return updated;
}

export default function SessionPage() {
  const params = useParams<{ id?: string | string[] }>();
  const searchParams = useSearchParams();
  const routeSessionId = getRouteSessionId(params.id);
  const shouldAutoStart = searchParams.get("start") === "1";
  const hasTriggeredAutoStartRef = useRef(false);

  const [truthMapOpen, setTruthMapOpen] = useState(false);
  const [sessionId, setSessionId] = useState(routeSessionId ?? "");
  const [phase, setPhase] = useState<SessionPhase>("CLARIFICATION");
  const [frictionLevel, setFrictionLevel] = useState(50);
  const [coreIdea, setCoreIdea] = useState("");
  const [messages, setMessages] = useState<ThreadViewMessage[]>([]);
  const [realtimeStatus, setRealtimeStatus] = useState<"connecting" | "connected" | "unavailable">("connecting");
  const [roundStartState, setRoundStartState] = useState<RoundStartState>("idle");
  const [followUpPending, setFollowUpPending] = useState(false);
  const [agentCountAtFollowUp, setAgentCountAtFollowUp] = useState(0);

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

  const postModeratorMessage = useCallback(async (message: string) => {
    const targetSessionId = sessionId || routeSessionId;
    if (!targetSessionId) {
      throw new Error("Cannot post moderator message without a session id.");
    }

    const currentModeratorCount = messages.filter(
      (entry) => entry.type === "agent" && normalizeAgentId(entry.agentId) === MODERATOR_AGENT_ID,
    ).length;
    setAgentCountAtFollowUp(currentModeratorCount);
    setFollowUpPending(true);

    logger.info("posting moderator message", {
      sessionId: targetSessionId,
      phase,
      messageLength: message.length,
    });

    const response = await fetch(`${BACKEND_API_PREFIX}/sessions/${targetSessionId}/messages`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ message }),
    });

    if (!response.ok) {
      let errorDetail = `Post session message failed with status ${response.status}`;
      try {
        const errorPayload = await readJsonResponse<{ error?: string }>(
          response,
          "Post session message",
        );
        if (errorPayload.error) {
          errorDetail = `Post session message failed (${response.status}): ${errorPayload.error}`;
        }
      } catch {
        // Keep HTTP-status-based detail if payload isn't valid JSON.
      }

      throw new Error(errorDetail);
    }

    const payload = await readJsonResponse<BackendSessionMessageResponse>(
      response,
      "Post session message",
    );

    if (payload.phase) {
      setPhase(mapBackendPhaseToSessionPhase(payload.phase));
    }

    logger.info("moderator message accepted", {
      sessionId: targetSessionId,
      routedAgentId: payload.routedAgentId,
      patchApplied: payload.patchApplied,
    });

    // SignalR normally streams the new transcript entries; if unavailable, resync via REST.
    if (realtimeStatus !== "connected") {
      await loadSessionState(targetSessionId);
    }
  }, [sessionId, routeSessionId, phase, realtimeStatus, loadSessionState, messages]);

  useEffect(() => {
    if (!followUpPending) return;
    const currentModeratorCount = messages.filter(
      (entry) => entry.type === "agent" && normalizeAgentId(entry.agentId) === MODERATOR_AGENT_ID,
    ).length;
    if (currentModeratorCount > agentCountAtFollowUp) {
      setFollowUpPending(false);
    }
  }, [followUpPending, agentCountAtFollowUp, messages]);

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

    connection.onTranscriptMessage((event: TranscriptMessageEvent) => {
      const mapped = mapTranscriptMessage(event, `stream-${event.id}`);
      if (!mapped) {
        return;
      }

      setRealtimeStatus("connected");
      setMessages((current) => upsertTranscriptMessage(current, mapped));
      logger.info("received transcript message event", {
        sessionId: routeSessionId,
        messageId: event.id,
        type: event.type,
        agentId: event.agentId ?? "system",
        round: event.round,
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
      .then((started) => {
        if (started) {
          setRealtimeStatus("connected");
          return;
        }

        setRealtimeStatus("unavailable");
        logger.warn(
          "signalr unavailable, continuing with rest-only session state",
          { sessionId: routeSessionId },
        );
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

  useEffect(() => {
    if (!routeSessionId || !shouldAutoStart) return;
    if (hasTriggeredAutoStartRef.current) return;
    if (realtimeStatus !== "connected") return;

    hasTriggeredAutoStartRef.current = true;
    setRoundStartState("starting");

    void fetch(`${BACKEND_API_PREFIX}/sessions/${routeSessionId}/start`, {
      method: "POST",
    })
      .then(async (response) => {
        if (!response.ok) {
          throw new Error(`Start session failed with status ${response.status}`);
        }

        setRoundStartState("started");
        logger.info("auto-start request accepted", { sessionId: routeSessionId });
      })
      .catch((error) => {
        setRoundStartState("failed");
        logger.error("failed to auto-start session from route", { sessionId: routeSessionId }, error);
      });
  }, [routeSessionId, shouldAutoStart, realtimeStatus]);

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
        roundStartState={roundStartState}
        messages={messages}
        pendingFollowUp={followUpPending}
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
        onSubmitMessage={postModeratorMessage}
      />
    </div>
  );
}
