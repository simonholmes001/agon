"use client";

import Link from "next/link";
import {
  ArrowLeft,
  Map,
  Flame,
  Info,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Slider } from "@/components/ui/slider";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { PHASE_LABELS, getFrictionLabel } from "@/lib/constants";
import type { SessionPhase } from "@/types";

interface SessionHeaderProps {
  sessionId: string;
  phase: SessionPhase;
  frictionLevel: number;
  onFrictionChange: (value: number) => void;
  onToggleTruthMap: () => void;
  truthMapOpen: boolean;
}

export default function SessionHeader({
  phase,
  frictionLevel,
  onFrictionChange,
  onToggleTruthMap,
  truthMapOpen,
}: SessionHeaderProps) {
  const frictionInfo = getFrictionLabel(frictionLevel);
  const phaseLabel = PHASE_LABELS[phase] ?? phase;

  return (
    <header className="flex flex-col border-b border-border/50 bg-card/30 backdrop-blur-sm">
      {/* Top row */}
      <div className="flex items-center gap-2 px-3 py-2 sm:px-4">
        <Button variant="ghost" size="icon" className="h-8 w-8" asChild>
          <Link href="/">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>

        <div className="flex flex-1 items-center gap-2 overflow-hidden">
          <h1 className="truncate text-sm font-semibold">Agon</h1>
          <Badge
            variant="secondary"
            className="shrink-0 text-xs font-normal"
          >
            {phaseLabel}
          </Badge>
        </div>

        {/* Friction — compact on mobile, expanded on desktop */}
        <div className="hidden items-center gap-2 sm:flex">
          <Tooltip>
            <TooltipTrigger asChild>
              <div className="flex cursor-help items-center gap-1.5 text-xs text-muted-foreground">
                <Flame className="h-3.5 w-3.5" />
                <span>{frictionInfo.label}</span>
                <Info className="h-3 w-3 opacity-50" />
              </div>
            </TooltipTrigger>
            <TooltipContent side="bottom" className="max-w-xs">
              <p className="text-xs">{frictionInfo.description}</p>
            </TooltipContent>
          </Tooltip>
          <Slider
            value={[frictionLevel]}
            onValueChange={([v]) => onFrictionChange(v)}
            min={0}
            max={100}
            step={1}
            className="w-28"
          />
          <span className="w-6 text-right font-mono text-xs text-muted-foreground">
            {frictionLevel}
          </span>
        </div>

        <Button
          variant={truthMapOpen ? "secondary" : "ghost"}
          size="icon"
          className="h-8 w-8"
          onClick={onToggleTruthMap}
        >
          <Map className="h-4 w-4" />
        </Button>
      </div>

      {/* Friction row — mobile only */}
      <div className="flex items-center gap-3 border-t border-border/30 px-4 py-2 sm:hidden">
        <Flame className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
        <Slider
          value={[frictionLevel]}
          onValueChange={([v]) => onFrictionChange(v)}
          min={0}
          max={100}
          step={1}
          className="flex-1"
        />
        <span className="w-8 text-right text-xs text-muted-foreground">
          {frictionInfo.label}
        </span>
      </div>
    </header>
  );
}
