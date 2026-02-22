"use client";

import { useState, useRef } from "react";
import { Send } from "lucide-react";
import { Button } from "@/components/ui/button";
import { createLogger } from "@/lib/logger";
import type { SessionPhase } from "@/types";

const logger = createLogger("MessageComposer");

interface MessageComposerProps {
  readonly sessionId: string;
  readonly phase: SessionPhase;
}

function getPlaceholder(phase: SessionPhase): string {
  switch (phase) {
    case "CLARIFICATION":
      return "Answer the clarifying questions…";
    case "DEBATE_ROUND_1":
    case "DEBATE_ROUND_2":
      return "Message the Council Moderator. They will coordinate with the council…";
    case "SYNTHESIS":
    case "TARGETED_LOOP":
      return "The council is synthesising…";
    case "POST_DELIVERY":
      return "Message the Council Moderator for follow-up decisions, challenges, or deep dives…";
    default:
      return "Type a message…";
  }
}

function isInputDisabled(phase: SessionPhase): boolean {
  return phase === "SYNTHESIS" || phase === "TARGETED_LOOP" || phase === "DELIVER" || phase === "DELIVER_WITH_GAPS";
}

export default function MessageComposer({ sessionId, phase }: MessageComposerProps) {
  const [message, setMessage] = useState("");
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const disabled = isInputDisabled(phase);

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!message.trim() || disabled) return;

    // Placeholder until backend POST /sessions/{sessionId}/messages is available
    logger.info("message sent", { sessionId, length: message.trim().length });
    setMessage("");

    if (textareaRef.current) {
      textareaRef.current.style.height = "auto";
    }
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSubmit(e);
    }
  }

  function handleInput(e: React.ChangeEvent<HTMLTextAreaElement>) {
    setMessage(e.target.value);
    // Auto-resize
    const el = e.target;
    el.style.height = "auto";
    el.style.height = `${Math.min(el.scrollHeight, 160)}px`;
  }

  return (
    <div className="border-t border-border/50 bg-card/30 backdrop-blur-sm">
      {(phase === "DEBATE_ROUND_1" || phase === "DEBATE_ROUND_2" || phase === "POST_DELIVERY") ? (
        <div className="mx-auto max-w-3xl px-4 pt-3 text-xs text-muted-foreground sm:px-6">
          Council Moderator channel
        </div>
      ) : null}
      <form
        onSubmit={handleSubmit}
        className="mx-auto flex max-w-3xl items-end gap-2 px-4 py-3 sm:px-6"
      >
        <textarea
          ref={textareaRef}
          value={message}
          onChange={handleInput}
          onKeyDown={handleKeyDown}
          placeholder={getPlaceholder(phase)}
          disabled={disabled}
          rows={1}
          className="max-h-40 min-h-[44px] flex-1 resize-none rounded-xl border border-border/50 bg-background px-4 py-3 text-sm leading-relaxed placeholder:text-muted-foreground/50 focus:border-primary/50 focus:outline-none focus:ring-1 focus:ring-primary/20 disabled:cursor-not-allowed disabled:opacity-50"
        />
        <Button
          type="submit"
          size="icon"
          className="h-11 w-11 shrink-0 rounded-xl"
          disabled={!message.trim() || disabled}
        >
          <Send className="h-4 w-4" />
        </Button>
      </form>
    </div>
  );
}
