"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { motion } from "framer-motion";
import { ArrowDown, Loader2 } from "lucide-react";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Button } from "@/components/ui/button";
import AgentMessageCard from "@/components/session/agent-message-card";
import { AGENTS, PHASE_LABELS } from "@/lib/constants";
import type { AgentId, SessionPhase } from "@/types";

type RealtimeStatus = "connecting" | "connected" | "unavailable";
type RoundStartState = "idle" | "starting" | "started" | "failed";

export interface ThreadViewMessage {
  id: string;
  type: "agent" | "system" | "user";
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
  roundStartState: RoundStartState;
  messages: ThreadViewMessage[];
  pendingFollowUp?: boolean;
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
  roundStartState,
  messages,
  pendingFollowUp = false,
}: Readonly<ThreadViewProps>) {
  const scrollAreaRef = useRef<HTMLDivElement>(null);
  const viewportRef = useRef<HTMLDivElement | null>(null);
  const bottomRef = useRef<HTMLDivElement>(null);
  const [autoScrollEnabled, setAutoScrollEnabled] = useState(true);
  const [hasUserScrolled, setHasUserScrolled] = useState(false);
  const lastScrollTopRef = useRef(0);

  useEffect(() => {
    if (!autoScrollEnabled) return;
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [phase, coreIdea, realtimeStatus, messages, autoScrollEnabled]);

  const updateScrollState = useCallback(() => {
    const viewport = viewportRef.current;
    if (!viewport) return;

    const currentTop = viewport.scrollTop;
    const scrolledUp = currentTop < lastScrollTopRef.current;
    lastScrollTopRef.current = currentTop;

    if (scrolledUp && autoScrollEnabled) {
      setAutoScrollEnabled(false);
      setHasUserScrolled(true);
    }
  }, [autoScrollEnabled]);

  useEffect(() => {
    const root = scrollAreaRef.current;
    if (!root) return;

    const viewport = root.querySelector<HTMLDivElement>(
      "[data-slot='scroll-area-viewport']",
    );
    if (!viewport) return;

    viewportRef.current = viewport;
    const handleScroll = () => updateScrollState();
    const handleUserIntent = () => {
      if (autoScrollEnabled) {
        setAutoScrollEnabled(false);
        setHasUserScrolled(true);
      }
    };
    viewport.addEventListener("scroll", handleScroll, { passive: true });
    viewport.addEventListener("wheel", handleUserIntent, { passive: true });
    viewport.addEventListener("touchstart", handleUserIntent, { passive: true });
    updateScrollState();

    return () => {
      viewport.removeEventListener("scroll", handleScroll);
      viewport.removeEventListener("wheel", handleUserIntent);
      viewport.removeEventListener("touchstart", handleUserIntent);
    };
  }, [updateScrollState, autoScrollEnabled]);

  const phaseLabel = PHASE_LABELS[phase] ?? "In progress";

  const realtimeMessage =
    realtimeStatus === "connected"
      ? "Live council updates connected."
      : realtimeStatus === "unavailable"
        ? "Real-time updates unavailable. Showing latest backend snapshot."
        : "Connecting to real-time updates…";
  const hasMessages = messages.length > 0;
  const isDebatePhase = phase === "DEBATE_ROUND_1" || phase === "DEBATE_ROUND_2";
  const shouldShowRoundProgressHint = isDebatePhase && !hasMessages;
  const hasModeratorSummary = messages.some(
    (message) => normalizeAgentId(message.agentId) === "synthesis-validation",
  );
  const shouldShowSummaryPending = hasMessages && !hasModeratorSummary;
  const shouldShowProcessingIndicator =
    (roundStartState === "starting" && !hasMessages) || pendingFollowUp;
  const roundStartMessage =
    roundStartState === "starting"
      ? "Launching Round 1. Agent responses will stream here as they complete."
      : roundStartState === "started"
        ? "Round complete. Review the moderator synthesis below, then challenge one point in the moderator channel."
      : roundStartState === "failed"
        ? "Round start failed. Please retry from this session."
        : null;

  const showJumpToLatest = hasMessages && hasUserScrolled && !autoScrollEnabled;

  const handleJumpToLatest = () => {
    setAutoScrollEnabled(true);
    setHasUserScrolled(false);
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  };

  return (
    <div className="relative flex h-full min-h-0 flex-1">
      <ScrollArea ref={scrollAreaRef} className="h-full w-full">
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

          {roundStartMessage ? (
            <motion.div
              key="round-start-status"
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.18, duration: 0.35, ease: "easeOut" as const }}
              className="rounded-2xl border border-primary/20 bg-primary/5 p-4 sm:p-5"
            >
              <p className="text-sm font-medium">Council launch status</p>
              <p className="mt-1 text-sm text-muted-foreground">{roundStartMessage}</p>
            </motion.div>
          ) : null}

          {shouldShowRoundProgressHint ? (
            <motion.div
              key="round-progress-hint"
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.2, duration: 0.35, ease: "easeOut" as const }}
              className="rounded-2xl border border-border/60 bg-card/40 p-4 sm:p-5"
            >
              <p className="text-sm font-medium">Council is analyzing now</p>
              <p className="mt-1 text-sm text-muted-foreground">
                You are already in {phaseLabel}. Agent responses will stream into this thread as each call completes.
              </p>
            </motion.div>
          ) : null}

          {hasMessages
            ? messages.map((message, index) => {
              const normalizedAgentId = normalizeAgentId(message.agentId);
              const key = `${message.id}-${index}`;

              if (message.type === "user") {
                return (
                  <motion.div
                    key={key}
                    initial={{ opacity: 0, y: 8 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ delay: 0.2, duration: 0.35, ease: "easeOut" as const }}
                    className="flex justify-end"
                  >
                    <div className="max-w-[85%] rounded-2xl border border-primary/40 bg-gradient-to-br from-primary/15 to-primary/5 px-4 py-3 text-sm text-foreground shadow-sm">
                      <p className="text-xs font-semibold uppercase tracking-wide text-primary/80">
                        Your follow-up
                      </p>
                      <p className="mt-1 text-sm">{message.content}</p>
                    </div>
                  </motion.div>
                );
              }

              if (message.type === "agent" && normalizedAgentId) {
                const isModeratorSummary = normalizedAgentId === "synthesis-validation";
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
                      variant={isModeratorSummary ? "moderatorSummary" : "default"}
                    />
                  </motion.div>
                );
              }

              if (isFollowUpSystemMessage(message.content)) {
                return (
                  <motion.div
                    key={key}
                    initial={{ opacity: 0, y: 8 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ delay: 0.2, duration: 0.35, ease: "easeOut" as const }}
                  >
                    <div className="rounded-2xl border border-primary/30 bg-primary/10 px-4 py-3 text-sm text-foreground/90">
                      <p className="text-xs font-semibold uppercase tracking-wide text-primary/80">
                        Moderator routing
                      </p>
                      <p className="mt-1">{message.content}</p>
                    </div>
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

          {shouldShowProcessingIndicator ? (
            <motion.div
              key="processing-indicator"
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.22, duration: 0.35, ease: "easeOut" as const }}
              className="rounded-2xl border border-primary/20 bg-primary/5 p-4 sm:p-5"
            >
              <div className="flex items-center gap-3">
                <div className="flex h-9 w-9 items-center justify-center rounded-full bg-primary/10">
                  <Loader2 className="h-4 w-4 animate-spin text-primary" />
                </div>
                <div>
                  <p className="text-sm font-medium">Council is processing</p>
                  <p className="text-sm text-muted-foreground">
                    Waiting for the first streaming response to arrive.
                  </p>
                </div>
              </div>
              <div className="mt-3 h-1.5 w-full overflow-hidden rounded-full bg-primary/10">
                <div className="h-full w-1/3 animate-pulse rounded-full bg-primary/40" />
              </div>
            </motion.div>
          ) : null}

          {shouldShowSummaryPending ? (
            <motion.div
              key="summary-pending"
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.22, duration: 0.35, ease: "easeOut" as const }}
              className="rounded-2xl border border-agent-synthesis/25 bg-agent-synthesis/5 p-4 sm:p-5"
            >
              <p className="text-sm font-semibold">Moderator synthesis pending</p>
              <p className="mt-1 text-sm text-muted-foreground">
                The moderator is consolidating the council responses into a single summary.
              </p>
            </motion.div>
          ) : null}

          <div ref={bottomRef} />
        </div>
      </ScrollArea>

      {showJumpToLatest ? (
        <div className="pointer-events-none absolute bottom-6 left-1/2 z-10 flex -translate-x-1/2">
          <Button
            type="button"
            size="sm"
            className="pointer-events-auto gap-2 rounded-full"
            onClick={handleJumpToLatest}
          >
            <ArrowDown className="h-4 w-4" />
            Jump to latest
          </Button>
        </div>
      ) : null}
    </div>
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
