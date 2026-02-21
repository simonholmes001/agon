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
import type { AgentId } from "@/types";

interface AgentMessageCardProps {
  readonly agentId: AgentId;
  readonly content: string;
  readonly isStreaming: boolean;
  readonly round: number;
  readonly isContested?: boolean;
}

function AgentMessageCardInner({
  agentId,
  content,
  isStreaming,
  isContested,
}: AgentMessageCardProps) {
  const agent = AGENTS[agentId];

  return (
    <div className="group relative rounded-2xl border border-border/40 bg-card/60 p-4 backdrop-blur-sm transition-colors hover:border-border/60 sm:p-5">
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
      <div className="prose prose-sm prose-neutral dark:prose-invert max-w-none text-sm leading-relaxed">
        <Markdown remarkPlugins={[remarkGfm]}>{content}</Markdown>
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
