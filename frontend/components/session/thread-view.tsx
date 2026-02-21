"use client";

import { useRef, useEffect } from "react";
import { motion } from "framer-motion";
import { ScrollArea } from "@/components/ui/scroll-area";
import AgentMessageCard from "@/components/session/agent-message-card";
import { AGENTS, PHASE_LABELS } from "@/lib/constants";
import type { AgentId, SessionPhase } from "@/types";

type RealtimeStatus = "connecting" | "connected" | "unavailable";

export interface ThreadViewMessage {
  id: string;
  type: "agent" | "system";
  content: string;
  agentId?: string;
  round?: number;
  isStreaming?: boolean;
}

interface ThreadViewProps {
  sessionId: string;
  phase: SessionPhase;
  coreIdea: string;
  realtimeStatus: RealtimeStatus;
  messages: ThreadViewMessage[];
}

function normalizeAgentId(agentId: string | undefined): AgentId | undefined {
  if (!agentId) return undefined;
  const normalizedAgentId = agentId.trim().toLowerCase().replace(/_/g, "-");
  return normalizedAgentId in AGENTS
    ? normalizedAgentId as AgentId
    : undefined;
}

export default function ThreadView({
  sessionId,
  phase,
  coreIdea,
  realtimeStatus,
  messages,
}: Readonly<ThreadViewProps>) {
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [phase, coreIdea, realtimeStatus, messages]);

  const phaseLabel = PHASE_LABELS[phase] ?? "In progress";

  const realtimeMessage =
    realtimeStatus === "connected"
      ? "Live council updates connected."
      : realtimeStatus === "unavailable"
        ? "Real-time updates unavailable. Showing latest backend snapshot."
        : "Connecting to real-time updates…";
  const hasMessages = messages.length > 0;

  return (
    <ScrollArea className="flex-1">
      <div className="mx-auto flex w-full max-w-3xl flex-col gap-4 px-4 py-6 sm:px-6">
        <motion.div
          key="session-loaded"
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.35, ease: "easeOut" as const }}
        >
          <SystemMessage content={`Session ${sessionId} loaded from backend.`} />
        </motion.div>

        <motion.div
          key="session-summary"
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.08, duration: 0.35, ease: "easeOut" as const }}
          className="rounded-2xl border border-border/50 bg-card/60 p-4 backdrop-blur-sm sm:p-5"
        >
          <p className="text-xs uppercase tracking-wide text-muted-foreground">
            Current Phase
          </p>
          <p className="mt-1 text-sm font-medium">{phaseLabel}</p>

          <p className="mt-4 text-xs uppercase tracking-wide text-muted-foreground">
            Core Idea From Backend
          </p>
          <p className="mt-1 text-sm leading-relaxed text-foreground/90">
            {coreIdea || "No core idea has been returned yet."}
          </p>
        </motion.div>

        <motion.div
          key="realtime-status"
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.14, duration: 0.35, ease: "easeOut" as const }}
        >
          <SystemMessage content={realtimeMessage} />
        </motion.div>

        {hasMessages
          ? messages.map((message, index) => {
            const normalizedAgentId = normalizeAgentId(message.agentId);
            const key = `${message.id}-${index}`;

            if (message.type === "agent" && normalizedAgentId) {
              return (
                <motion.div
                  key={key}
                  initial={{ opacity: 0, y: 8 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ delay: 0.2, duration: 0.35, ease: "easeOut" as const }}
                >
                  <AgentMessageCard
                    agentId={normalizedAgentId}
                    content={message.content}
                    isStreaming={Boolean(message.isStreaming)}
                    round={message.round ?? 0}
                  />
                </motion.div>
              );
            }

            return (
              <motion.div
                key={key}
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.2, duration: 0.35, ease: "easeOut" as const }}
              >
                <SystemMessage content={message.content} />
              </motion.div>
            );
          })
          : (
            <motion.div
              key="waiting-state"
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.2, duration: 0.35, ease: "easeOut" as const }}
              className="rounded-2xl border border-dashed border-border/60 bg-card/40 p-4 sm:p-5"
            >
              <p className="text-sm font-medium">Awaiting agent transcript</p>
              <p className="mt-1 text-sm text-muted-foreground">
                No agent transcript has been streamed yet. This view now reflects
                live backend state and no longer uses frontend mock responses.
              </p>
            </motion.div>
          )}

        <div ref={bottomRef} />
      </div>
    </ScrollArea>
  );
}

function SystemMessage({ content }: Readonly<{ content: string }>) {
  return (
    <div className="flex justify-center">
      <p className="rounded-full bg-muted/50 px-4 py-1.5 text-xs text-muted-foreground">
        {content}
      </p>
    </div>
  );
}
