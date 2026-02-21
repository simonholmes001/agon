"use client";

import { useRef, useEffect } from "react";
import { motion } from "framer-motion";
import { ScrollArea } from "@/components/ui/scroll-area";
import AgentMessageCard from "@/components/session/agent-message-card";
import type { AgentId } from "@/types";

// Demo messages — will be replaced by real SignalR streaming
const DEMO_MESSAGES = [
  {
    id: "sys-1",
    type: "system" as const,
    content: "Session started. The Socratic Clarifier is reviewing your idea…",
    createdAt: new Date().toISOString(),
  },
  {
    id: "msg-1",
    type: "agent" as const,
    agentId: "socratic-clarifier" as AgentId,
    round: 0,
    content: `I've reviewed your idea. Before we proceed, I need to clarify a few things to ensure the council can give you the most useful analysis.

**Here are my questions:**

1. **Who is your primary user?** You mentioned "freelancers" — are we talking about solo consultants, creative agencies, or both? The product strategy changes significantly based on this.

2. **What's your budget and timeline?** Are you bootstrapping, or do you have funding? Is there a launch deadline?

3. **What does success look like in 90 days?** First paying customer? A specific number of signups? A working MVP you can demo?`,
    isStreaming: false,
    createdAt: new Date().toISOString(),
  },
];

interface ThreadViewProps {
  sessionId: string;
}

export default function ThreadView({ sessionId }: Readonly<ThreadViewProps>) {
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, []);

  return (
    <ScrollArea className="flex-1">
      <div className="mx-auto flex w-full max-w-3xl flex-col gap-4 px-4 py-6 sm:px-6">
        {DEMO_MESSAGES.map((msg, i) => (
          <motion.div
            key={msg.id}
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{
              delay: i * 0.15,
              duration: 0.4,
              ease: "easeOut" as const,
            }}
          >
            {msg.type === "system" ? (
              <SystemMessage content={msg.content} />
            ) : (
              <AgentMessageCard
                agentId={msg.agentId}
                content={msg.content}
                isStreaming={msg.isStreaming}
                round={msg.round}
              />
            )}
          </motion.div>
        ))}
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
