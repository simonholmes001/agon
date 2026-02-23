"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
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

const MODERATOR_AGENT_ID = "synthesis-validation";

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

function isModeratorAgentMessage(message: ThreadViewMessage, phase: SessionPhase): boolean {
  if (message.type !== "agent") return false;
  const normalized = normalizeAgentId(message.agentId);
  if (normalized === MODERATOR_AGENT_ID) return true;
  if (phase === "CLARIFICATION" && normalized === "socratic-clarifier") return true;
  return false;
}

function shouldHideSystemMessage(content: string): boolean {
  const normalized = content.trim().toLowerCase();
  return normalized.startsWith("moderator routed follow-up")
    || normalized.startsWith("round 1 started")
    || normalized.startsWith("round 2 started")
    || normalized.startsWith("round 1 complete")
    || normalized.startsWith("round 2 complete");
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
  const [processingStepIndex, setProcessingStepIndex] = useState(0);

  useEffect(() => {
    if (!autoScrollEnabled) return;
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [phase, coreIdea, realtimeStatus, messages, autoScrollEnabled]);

  const updateScrollState = useCallback(() => {
    const viewport = viewportRef.current;
    if (!viewport) return;

    const distanceFromBottom = viewport.scrollHeight - (viewport.scrollTop + viewport.clientHeight);
    const isAtBottom = distanceFromBottom <= 24;
    const userBreakThreshold = 120;

    if (isAtBottom) {
      if (!autoScrollEnabled) {
        setAutoScrollEnabled(true);
        setHasUserScrolled(false);
      }
      return;
    }

    if (autoScrollEnabled && distanceFromBottom > userBreakThreshold) {
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

  const visibleMessages = messages.filter((message) => {
    if (message.type === "agent") {
      return isModeratorAgentMessage(message, phase);
    }

    if (message.type === "system") {
      return !shouldHideSystemMessage(message.content);
    }

    return true;
  });
  const hasVisibleMessages = visibleMessages.length > 0;
  const hasAnyAgentMessages = messages.some((message) => message.type === "agent");
  const isDebatePhase = phase === "DEBATE_ROUND_1" || phase === "DEBATE_ROUND_2";
  const shouldShowRoundProgressHint = isDebatePhase && !hasAnyAgentMessages;
  const hasModeratorSummary = messages.some(
    (message) => normalizeAgentId(message.agentId) === MODERATOR_AGENT_ID,
  );
  const shouldShowProcessingIndicator =
    (roundStartState === "starting" && !hasAnyAgentMessages)
    || pendingFollowUp
    || (!hasAnyAgentMessages && (phase === "POST_DELIVERY" || phase === "CLARIFICATION"))
    || (hasAnyAgentMessages && !hasModeratorSummary);
  const showJumpToLatest = hasVisibleMessages && hasUserScrolled && !autoScrollEnabled;

  const handleJumpToLatest = () => {
    setAutoScrollEnabled(true);
    setHasUserScrolled(false);
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  };

  const processingSteps = useMemo(() => {
    if (pendingFollowUp) {
      return [
        "Moderator is reviewing your follow-up question…",
        "Moderator is drafting the response…",
        "Moderator is preparing the streamed summary…",
      ];
    }

    if (phase === "CLARIFICATION") {
      return [
        "Socratic Clarifier is reviewing your question…",
        "Socratic Clarifier is drafting clarifying questions…",
      ];
    }

    if (phase === "POST_DELIVERY") {
      return [
        "Moderator is reviewing your proposal…",
        "Moderator is synthesizing the council output…",
        "Moderator is refining recommendations…",
      ];
    }

    return [
      "Agent 1 (Framing Challenger) is stress-testing your proposal…",
      "Agent 2 (Product Strategist) is shaping the MVP scope…",
      "Agent 3 (Technical Architect) is outlining the system design…",
      "Agent 4 (Contrarian) is probing risks and failure modes…",
      "Agent 5 (Research Librarian) is gathering evidence…",
      "Moderator is synthesizing the council output…",
    ];
  }, [pendingFollowUp, phase]);

  useEffect(() => {
    if (!shouldShowProcessingIndicator) return;
    setProcessingStepIndex(0);
    if (processingSteps.length <= 1) return;

    const interval = setInterval(() => {
      setProcessingStepIndex((current) => (current + 1) % processingSteps.length);
    }, 1600);

    return () => clearInterval(interval);
  }, [shouldShowProcessingIndicator, processingSteps]);

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
            className="rounded-2xl border border-border/40 bg-card/60 p-4 backdrop-blur-sm sm:p-5"
          >
            <p className="text-xs uppercase tracking-wide text-muted-foreground">
              Current Phase
            </p>
            <p className="mt-1 text-sm font-medium">{phaseLabel}</p>
          </motion.div>

          {coreIdea ? (
            <motion.div
              key="core-question"
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.12, duration: 0.35, ease: "easeOut" as const }}
              className="flex justify-end"
            >
              <div className="max-w-[85%] rounded-2xl border border-amber-200/70 bg-gradient-to-br from-amber-50 via-rose-50 to-emerald-50 px-4 py-3 text-sm text-foreground shadow-sm dark:border-amber-400/30 dark:from-amber-500/15 dark:via-slate-950/70 dark:to-emerald-500/15">
                <p className="text-xs font-semibold uppercase tracking-wide text-amber-700/80 dark:text-amber-200/80">
                  Your question
                </p>
                <p className="mt-1 text-sm leading-relaxed">{coreIdea}</p>
              </div>
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

          {hasVisibleMessages
            ? visibleMessages.map((message, index) => {
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
                const isModeratorSummary =
                  normalizedAgentId === MODERATOR_AGENT_ID
                  || (phase === "CLARIFICATION" && normalizedAgentId === "socratic-clarifier");
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
                <p className="text-sm font-medium">Awaiting moderator response</p>
                <p className="mt-1 text-sm text-muted-foreground">
                  The moderator response will stream here as soon as it is available.
                </p>
              </motion.div>
            )}

          {shouldShowProcessingIndicator ? (
            <motion.div
              key="processing-indicator"
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.22, duration: 0.35, ease: "easeOut" as const }}
              className="rounded-2xl border border-emerald-200/60 bg-gradient-to-br from-emerald-50 via-amber-50/70 to-rose-50/60 p-4 sm:p-5 dark:border-emerald-400/20 dark:from-emerald-500/10 dark:via-slate-950/60 dark:to-amber-500/10"
            >
              <div className="flex items-center gap-3">
                <div className="flex h-9 w-9 items-center justify-center rounded-full bg-emerald-100/80 dark:bg-emerald-500/10">
                  <Loader2 className="h-4 w-4 animate-spin text-emerald-700 dark:text-emerald-200" />
                </div>
                <div>
                  <p className="text-sm font-medium text-foreground">Council is processing</p>
                  <p className="text-sm text-muted-foreground">
                    Streaming starts as soon as the moderator begins responding.
                  </p>
                </div>
              </div>
              <div className="mt-3 rounded-xl border border-emerald-200/50 bg-background/70 px-3 py-2 text-sm text-foreground/80 dark:border-emerald-500/20">
                {processingSteps[processingStepIndex] ?? "Council is preparing a response…"}
              </div>
              <div className="mt-3 h-1.5 w-full overflow-hidden rounded-full bg-emerald-200/50 dark:bg-emerald-500/10">
                <div className="h-full w-1/3 animate-pulse rounded-full bg-emerald-400/70 dark:bg-emerald-300/40" />
              </div>
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
