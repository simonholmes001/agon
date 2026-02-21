"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { motion } from "framer-motion";
import { ArrowLeft, ArrowRight, Flame, Info, Sparkles } from "lucide-react";
import Link from "next/link";
import { Button } from "@/components/ui/button";
import { Slider } from "@/components/ui/slider";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { getFrictionLabel } from "@/lib/constants";
import { createLogger } from "@/lib/logger";
import ThemeToggle from "@/components/theme-toggle";

const logger = createLogger("NewSessionPage");
const BACKEND_API_PREFIX = "/api/backend";

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

export default function NewSessionPage() {
  const router = useRouter();
  const [idea, setIdea] = useState("");
  const [frictionLevel, setFrictionLevel] = useState(50);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const frictionInfo = getFrictionLabel(frictionLevel);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const trimmedIdea = idea.trim();
    if (!trimmedIdea || trimmedIdea.length < 10) return;

    setIsSubmitting(true);

    try {
      const createResponse = await fetch(`${BACKEND_API_PREFIX}/sessions`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          idea: trimmedIdea,
          mode: "deep",
          frictionLevel,
        }),
      });

      if (!createResponse.ok) {
        throw new Error(`Create session failed with status ${createResponse.status}`);
      }

      const createdSession = await readJsonResponse<{ sessionId?: string }>(
        createResponse,
        "Create session",
      );
      const sessionId = createdSession.sessionId;
      if (!sessionId) {
        throw new Error("Create session response did not include sessionId");
      }

      const startResponse = await fetch(
        `${BACKEND_API_PREFIX}/sessions/${sessionId}/start`,
        {
          method: "POST",
        },
      );
      if (!startResponse.ok) {
        throw new Error(`Start session failed with status ${startResponse.status}`);
      }

      router.push(`/session/${sessionId}`);
    } catch (error) {
      logger.error(
        "failed to create or start session",
        { frictionLevel, ideaLength: trimmedIdea.length },
        error,
      );
      setIsSubmitting(false);
    }
  }

  function handleIdeaKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key !== "Enter" || e.shiftKey) return;
    e.preventDefault();

    if (idea.trim().length < 10 || isSubmitting) return;
    e.currentTarget.form?.requestSubmit();
  }

  return (
    <div className="relative flex min-h-[100dvh] flex-col overflow-hidden bg-background">
      <div className="pointer-events-none absolute inset-0 -z-10">
        <div className="absolute left-1/2 top-[-180px] h-[480px] w-[480px] -translate-x-1/2 rounded-full bg-primary/10 blur-[120px]" />
        <div className="absolute bottom-[-140px] right-[10%] h-[360px] w-[360px] rounded-full bg-agent-socratic/10 blur-[110px]" />
        <div className="absolute bottom-[-180px] left-[5%] h-[320px] w-[320px] rounded-full bg-agent-contrarian/10 blur-[110px]" />
      </div>

      {/* Top bar */}
      <header className="flex items-center gap-3 border-b border-border/50 bg-background/70 px-4 py-3 backdrop-blur-sm sm:px-6">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div className="flex min-w-0 flex-1 items-center gap-2">
          <h1 className="truncate text-sm font-medium text-muted-foreground">
            New Session
          </h1>
          <span className="hidden rounded-full border border-border/60 bg-card/60 px-2 py-0.5 text-[11px] text-muted-foreground sm:inline-block">
            Guided kickoff
          </span>
        </div>
        <ThemeToggle />
      </header>

      {/* Form */}
      <main className="mx-auto flex w-full max-w-3xl flex-1 flex-col px-4 pb-28 pt-8 sm:px-6 sm:pb-32 sm:pt-14">
        <motion.div
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4, ease: "easeOut" as const }}
        >
          <div className="inline-flex items-center gap-2 rounded-full border border-border/60 bg-card/70 px-3 py-1 text-xs text-muted-foreground">
            <Sparkles className="h-3.5 w-3.5 text-agent-synthesis" />
            Strategic council kickoff
          </div>

          <h2 className="mt-4 text-3xl font-bold tracking-tight sm:text-4xl">
            <span className="bg-gradient-to-r from-agent-socratic via-agent-product to-agent-synthesis bg-clip-text text-transparent">
              What&apos;s your idea?
            </span>
          </h2>
          <p className="mt-3 max-w-2xl text-base text-muted-foreground sm:text-lg">
            Describe your idea, product concept, or strategic question. The
            council will take it from here.
          </p>
        </motion.div>

        <form
          id="new-session-form"
          onSubmit={handleSubmit}
          className="mt-8 flex flex-1 flex-col gap-7"
        >
          {/* Idea input */}
          <motion.div
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, delay: 0.1, ease: "easeOut" as const }}
          >
            <textarea
              value={idea}
              onChange={(e) => setIdea(e.target.value)}
              onKeyDown={handleIdeaKeyDown}
              placeholder="e.g. A mobile app that helps freelancers manage invoices and track payments, targeting solo consultants who currently use spreadsheets..."
              className="h-40 w-full resize-none rounded-2xl border border-border/70 bg-card/70 px-4 py-3 text-base leading-relaxed text-foreground shadow-sm placeholder:text-muted-foreground/50 focus:border-primary/50 focus:outline-none focus:ring-2 focus:ring-primary/20 sm:h-48"
              autoFocus
            />
            <p className="mt-2 text-xs text-muted-foreground">
              {idea.trim().length < 10
                ? `At least 10 characters (${idea.trim().length}/10)`
                : `${idea.trim().length} characters`}
            </p>
            <p className="mt-1 text-xs text-muted-foreground/80">
              Press Enter to launch. Use Shift+Enter for a new line.
            </p>
          </motion.div>

          {/* Friction slider */}
          <motion.div
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, delay: 0.2, ease: "easeOut" as const }}
            className="rounded-2xl border border-border/60 bg-card/70 p-6 shadow-sm"
          >
            <div className="flex items-center gap-2">
              <Flame className="h-4 w-4 text-muted-foreground" />
              <span className="text-sm font-medium">Friction Level</span>
              <Tooltip>
                <TooltipTrigger asChild>
                  <Info className="h-3.5 w-3.5 cursor-help text-muted-foreground/60" />
                </TooltipTrigger>
                <TooltipContent side="top" className="max-w-xs">
                  <p>
                    Controls both agent tone and convergence requirements. Higher
                    friction means more rigorous evidence standards and
                    adversarial challenge.
                  </p>
                </TooltipContent>
              </Tooltip>
              <span className="ml-auto font-mono text-sm text-muted-foreground">
                {frictionLevel}
              </span>
            </div>

            <Slider
              value={[frictionLevel]}
              onValueChange={([v]) => setFrictionLevel(v)}
              min={0}
              max={100}
              step={1}
              className="mt-4"
            />

            <div className="mt-3 flex items-center justify-between text-xs text-muted-foreground">
              <span>Brainstorm</span>
              <span>Balanced</span>
              <span>Adversarial</span>
            </div>

            <div className="mt-4 rounded-lg bg-muted/50 px-3 py-2">
              <p className="text-sm font-medium">{frictionInfo.label}</p>
              <p className="text-xs text-muted-foreground">
                {frictionInfo.description}
              </p>
            </div>
          </motion.div>
        </form>
      </main>

      <div
        data-testid="launch-action-bar"
        className="sticky bottom-0 z-20 border-t border-border/60 bg-background/85 px-4 pb-4 pt-3 backdrop-blur-md sm:px-6 sm:pb-5"
      >
        <div className="mx-auto flex w-full max-w-3xl items-center justify-end">
          <Button
            type="submit"
            form="new-session-form"
            size="lg"
            className="w-full gap-2 text-base sm:w-auto"
            disabled={idea.trim().length < 10 || isSubmitting}
          >
            {isSubmitting ? (
              <>
                <span className="h-4 w-4 animate-spin rounded-full border-2 border-primary-foreground border-t-transparent" />
                Starting session…
              </>
            ) : (
              <>
                Launch Council
                <ArrowRight className="h-4 w-4" />
              </>
            )}
          </Button>
        </div>
      </div>
    </div>
  );
}
