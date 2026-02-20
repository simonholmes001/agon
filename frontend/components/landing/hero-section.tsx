"use client";

import { motion } from "framer-motion";
import {
  ArrowRight,
  Sparkles,
  Shield,
  Target,
  Zap,
} from "lucide-react";
import Link from "next/link";
import { Button } from "@/components/ui/button";

const features = [
  {
    icon: Sparkles,
    title: "Multi-Model Council",
    description:
      "Seven specialist agents powered by GPT-5.2, Gemini 3, Claude Opus 4.6, and DeepSeek debate your idea from every angle.",
  },
  {
    icon: Shield,
    title: "Red-Team Built In",
    description:
      "A dedicated Contrarian agent stress-tests your idea — finding failure modes before reality does.",
  },
  {
    icon: Target,
    title: "Living Truth Map",
    description:
      "Every claim, risk, and assumption is tracked in a linked graph — not buried in a transcript.",
  },
  {
    icon: Zap,
    title: "Decision-Grade Output",
    description:
      "Walk away with a Verdict, PRD, Risk Registry, and actionable plan — not just conversation.",
  },
];

const fadeUp = {
  hidden: { opacity: 0, y: 20 },
  visible: (i: number) => ({
    opacity: 1,
    y: 0,
    transition: { delay: i * 0.1, duration: 0.5, ease: "easeOut" as const },
  }),
};

export default function HeroSection() {
  return (
    <section className="relative flex min-h-[100dvh] flex-col items-center justify-center overflow-hidden px-6 py-24">
      {/* Background gradient */}
      <div className="pointer-events-none absolute inset-0 -z-10">
        <div className="absolute left-1/2 top-0 h-[600px] w-[600px] -translate-x-1/2 rounded-full bg-primary/5 blur-[120px]" />
        <div className="absolute bottom-0 left-1/4 h-[400px] w-[400px] rounded-full bg-agent-socratic/5 blur-[100px]" />
        <div className="absolute bottom-0 right-1/4 h-[400px] w-[400px] rounded-full bg-agent-contrarian/5 blur-[100px]" />
      </div>

      {/* Hero content */}
      <motion.div
        className="mx-auto flex max-w-3xl flex-col items-center text-center"
        initial="hidden"
        animate="visible"
        variants={{
          visible: { transition: { staggerChildren: 0.1 } },
        }}
      >
        <motion.h1
          variants={fadeUp}
          custom={0}
          className="text-4xl font-bold leading-tight tracking-tight sm:text-5xl md:text-6xl lg:text-7xl"
        >
          <span className="bg-gradient-to-r from-agent-socratic via-agent-product to-agent-synthesis bg-clip-text text-transparent">
            Agon
          </span>
          <br />
          Stress-test your ideas
          <br />
          <span className="bg-gradient-to-r from-agent-socratic via-agent-product to-agent-synthesis bg-clip-text text-transparent">
            before reality does
          </span>
        </motion.h1>

        <motion.p
          variants={fadeUp}
          custom={1}
          className="mt-6 max-w-xl text-lg text-muted-foreground sm:text-xl"
        >
          A council of specialist AI agents debates, challenges, and refines
          your idea into a decision-grade output pack — with a living truth map
          you can steer.
        </motion.p>

        <motion.div
          variants={fadeUp}
          custom={2}
          className="mt-10 flex flex-col gap-4 sm:flex-row"
        >
          <Button asChild size="lg" className="gap-2 text-base">
            <Link href="/session/new">
              Start a Session
              <ArrowRight className="h-4 w-4" />
            </Link>
          </Button>
          <Button asChild variant="outline" size="lg" className="text-base">
            <Link href="/sessions">View Past Sessions</Link>
          </Button>
        </motion.div>
      </motion.div>

      {/* Features grid */}
      <motion.div
        className="mx-auto mt-24 grid max-w-5xl gap-6 sm:grid-cols-2 lg:grid-cols-4"
        initial="hidden"
        animate="visible"
        variants={{
          visible: { transition: { staggerChildren: 0.08, delayChildren: 0.4 } },
        }}
      >
        {features.map((feature, i) => (
          <motion.div
            key={feature.title}
            variants={fadeUp}
            custom={i}
            className="group relative rounded-2xl border border-border/50 bg-card/50 p-6 backdrop-blur-sm transition-colors hover:border-border hover:bg-card"
          >
            <div className="mb-4 inline-flex rounded-xl bg-muted p-3">
              <feature.icon className="h-5 w-5 text-foreground" />
            </div>
            <h3 className="mb-2 font-semibold">{feature.title}</h3>
            <p className="text-sm leading-relaxed text-muted-foreground">
              {feature.description}
            </p>
          </motion.div>
        ))}
      </motion.div>
    </section>
  );
}
