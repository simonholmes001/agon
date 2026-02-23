"use client";

import { memo } from "react";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { AlertTriangle, MoreHorizontal } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { AGENTS } from "@/lib/constants";
import { cn } from "@/lib/utils";
import type { AgentId } from "@/types";

interface AgentMessageCardProps {
  readonly agentId: AgentId;
  readonly content: string;
  readonly isStreaming: boolean;
  readonly round: number;
  readonly isContested?: boolean;
  readonly variant?: "default" | "moderatorSummary";
}

function AgentMessageCardInner({
  agentId,
  content,
  isStreaming,
  isContested,
  variant = "default",
}: AgentMessageCardProps) {
  const agent = AGENTS[agentId];
  const isModeratorSummary = variant === "moderatorSummary";
  const proseClassName = cn(
    "prose prose-sm prose-neutral max-w-none text-sm leading-relaxed",
    "prose-headings:font-semibold prose-strong:text-foreground",
    isModeratorSummary
      ? "prose-headings:tracking-tight prose-h2:text-base prose-h3:text-sm prose-p:leading-7"
      : "prose-h3:text-base prose-h4:text-sm",
    "dark:prose-invert",
  );

  return (
    <div
      className={cn(
        "group relative rounded-2xl border border-border/40 bg-card/60 p-4 backdrop-blur-sm transition-colors hover:border-border/60 sm:p-5",
        isModeratorSummary
          && "border-agent-socratic/35 bg-gradient-to-br from-agent-socratic/12 via-agent-product/10 to-agent-synthesis/12 shadow-sm dark:border-agent-socratic/35 dark:from-agent-socratic/18 dark:via-slate-950/85 dark:to-agent-synthesis/18",
      )}
    >
      {/* Header */}
      <div className="mb-3 flex items-center gap-3">
        {/* Agent avatar */}
        <div
          className="flex h-8 w-8 items-center justify-center rounded-full text-sm"
          style={{ backgroundColor: `${agent.color}20` }}
        >
          {agent.icon}
        </div>

        <div className="flex flex-1 flex-col">
          <div className="flex items-center gap-2">
            <span className="text-sm font-semibold">{agent.name}</span>
            {isModeratorSummary && (
              <Badge variant="secondary" className="text-[10px]">
                Moderator summary
              </Badge>
            )}
            {isContested && (
              <Tooltip>
                <TooltipTrigger asChild>
                  <AlertTriangle className="h-3.5 w-3.5 text-contested" />
                </TooltipTrigger>
                <TooltipContent>
                  Contains contested claims (confidence &lt; 0.3)
                </TooltipContent>
              </Tooltip>
            )}
          </div>
          <span className="text-xs text-muted-foreground">{agent.role}</span>
        </div>

        <Badge
          variant="outline"
          className="hidden text-[10px] font-normal sm:inline-flex"
          style={{
            borderColor: `${agent.color}40`,
            color: agent.color,
          }}
        >
          {agent.model}
        </Badge>

        {/* Actions */}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button
              variant="ghost"
              size="icon"
              className="h-7 w-7 opacity-0 transition-opacity group-hover:opacity-100"
            >
              <MoreHorizontal className="h-3.5 w-3.5" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-44">
            <DropdownMenuItem>Challenge this</DropdownMenuItem>
            <DropdownMenuItem>Ask why</DropdownMenuItem>
            <DropdownMenuItem>Deep dive</DropdownMenuItem>
            <DropdownMenuItem>Expand</DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      {/* Content */}
      <div className={proseClassName}>
        <Markdown
          remarkPlugins={[remarkGfm]}
          components={{
            h2: ({ children }) => (
              <h2 className="mt-5 border-b border-border/60 pb-1 text-base font-semibold text-foreground">
                {children}
              </h2>
            ),
            h3: ({ children }) => (
              <h3 className="mt-4 text-base font-semibold text-foreground">{children}</h3>
            ),
            h4: ({ children }) => (
              <h4 className="mt-3 text-sm font-semibold text-foreground/90">{children}</h4>
            ),
            h5: ({ children }) => (
              <h5 className="mt-3 text-sm font-semibold uppercase tracking-wide text-muted-foreground">
                {children}
              </h5>
            ),
            p: ({ children }) => (
              <p className="mt-2 leading-relaxed text-foreground/90">{children}</p>
            ),
            ul: ({ children }) => (
              <ul className="mt-2 list-disc space-y-1 pl-5">{children}</ul>
            ),
            ol: ({ children }) => (
              <ol className="mt-2 list-decimal space-y-1 pl-5">{children}</ol>
            ),
            li: ({ children }) => (
              <li className="leading-relaxed">{children}</li>
            ),
            hr: () => <hr className="my-4 border-border/60" />,
            blockquote: ({ children }) => (
              <blockquote className="mt-3 border-l-2 border-border/60 pl-4 text-muted-foreground">
                {children}
              </blockquote>
            ),
          }}
        >
          {content}
        </Markdown>
      </div>

      {/* Streaming indicator */}
      {isStreaming && (
        <div className="mt-3 flex items-center gap-2">
          <div className="flex gap-1">
            <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-primary/60" />
            <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-primary/60 [animation-delay:0.2s]" />
            <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-primary/60 [animation-delay:0.4s]" />
          </div>
          <span className="text-xs text-muted-foreground">Thinking…</span>
        </div>
      )}
    </div>
  );
}

const AgentMessageCard = memo(AgentMessageCardInner);
export default AgentMessageCard;
