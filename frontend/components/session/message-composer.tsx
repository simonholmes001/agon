"use client";

import { useState, useRef } from "react";
import { Send } from "lucide-react";
import { Button } from "@/components/ui/button";
import type { SessionPhase } from "@/types";

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
      return "Challenge a claim, ask a question, or add a constraint…";
    case "SYNTHESIS":
    case "TARGETED_LOOP":
      return "The council is synthesising…";
    case "POST_DELIVERY":
      return "Ask about the results, challenge a decision, or request a deep dive…";
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

    // TODO: POST /sessions/{sessionId}/messages
    console.log("Send message:", { sessionId, message });
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
