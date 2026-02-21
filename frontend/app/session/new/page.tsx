"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { motion } from "framer-motion";
import { ArrowLeft, ArrowRight, Flame, Info } from "lucide-react";
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

const logger = createLogger("NewSessionPage");

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
      const createResponse = await fetch("/sessions", {
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

      const createdSession = await createResponse.json() as { sessionId?: string };
      const sessionId = createdSession.sessionId;
      if (!sessionId) {
        throw new Error("Create session response did not include sessionId");
      }

      const startResponse = await fetch(`/sessions/${sessionId}/start`, {
        method: "POST",
      });
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

  return (
    <div className="flex min-h-[100dvh] flex-col bg-background">
      {/* Top bar */}
      <header className="flex items-center gap-3 border-b border-border/50 px-4 py-3 sm:px-6">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <h1 className="text-sm font-medium text-muted-foreground">
          New Session
        </h1>
      </header>

      {/* Form */}
      <main className="mx-auto flex w-full max-w-2xl flex-1 flex-col px-4 py-8 sm:px-6 sm:py-16">
        <motion.div
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4, ease: "easeOut" as const }}
        >
          <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">
            What&apos;s your idea?
          </h2>
          <p className="mt-2 text-muted-foreground">
            Describe your idea, product concept, or strategic question. The
            council will take it from here.
          </p>
        </motion.div>

        <form onSubmit={handleSubmit} className="mt-8 flex flex-1 flex-col gap-8">
          {/* Idea input */}
          <motion.div
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, delay: 0.1, ease: "easeOut" as const }}
          >
            <textarea
              value={idea}
              onChange={(e) => setIdea(e.target.value)}
              placeholder="e.g. A mobile app that helps freelancers manage invoices and track payments, targeting solo consultants who currently use spreadsheets..."
              className="h-40 w-full resize-none rounded-xl border border-border/50 bg-card/50 px-4 py-3 text-base leading-relaxed text-foreground placeholder:text-muted-foreground/50 focus:border-primary/50 focus:outline-none focus:ring-1 focus:ring-primary/20 sm:h-48"
              autoFocus
            />
            <p className="mt-2 text-xs text-muted-foreground">
              {idea.trim().length < 10
                ? `At least 10 characters (${idea.trim().length}/10)`
                : `${idea.trim().length} characters`}
            </p>
          </motion.div>

          {/* Friction slider */}
          <motion.div
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, delay: 0.2, ease: "easeOut" as const }}
            className="rounded-xl border border-border/50 bg-card/50 p-6"
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

          {/* Submit */}
          <motion.div
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, delay: 0.3, ease: "easeOut" as const }}
            className="mt-auto pt-4"
          >
            <Button
              type="submit"
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
          </motion.div>
        </form>
      </main>
    </div>
  );
}
