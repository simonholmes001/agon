"use client";

import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import { Badge } from "@/components/ui/badge";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Separator } from "@/components/ui/separator";
import {
  AlertTriangle,
  CheckCircle2,
  CircleDot,
  HelpCircle,
  ShieldAlert,
  Lightbulb,
} from "lucide-react";

interface TruthMapDrawerProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
}

// Demo Truth Map state — will be driven by SignalR TruthMapPatch events
const DEMO_CLAIMS = [
  {
    id: "c1",
    agent: "product-strategist",
    text: "Solo freelancers prioritise speed-to-invoice over feature depth",
    confidence: 0.82,
    status: "active" as const,
  },
  {
    id: "c2",
    agent: "technical-architect",
    text: "A mobile-first PWA would cover 90% of the use case without native app development cost",
    confidence: 0.71,
    status: "active" as const,
  },
  {
    id: "c3",
    agent: "contrarian",
    text: "The invoicing market is saturated — differentiation must come from the payment tracking, not invoicing",
    confidence: 0.24,
    status: "contested" as const,
  },
];

const DEMO_RISKS = [
  {
    id: "r1",
    text: "Stripe/PayPal integration complexity for payment tracking",
    severity: "high" as const,
    category: "technical" as const,
  },
  {
    id: "r2",
    text: "Low switching cost from spreadsheets — users may not see enough value",
    severity: "medium" as const,
    category: "market" as const,
  },
];

const DEMO_ASSUMPTIONS: { id: string; text: string; status: "unvalidated" | "validated" | "invalidated" }[] = [
  {
    id: "a1",
    text: "Freelancers currently track invoices in spreadsheets",
    status: "unvalidated",
  },
  {
    id: "a2",
    text: "Payment reminders would reduce average days-to-payment",
    status: "unvalidated",
  },
];

const DEMO_OPEN_QUESTIONS = [
  { id: "q1", text: "Who is the primary persona — solo or small team?", blocking: true },
  { id: "q2", text: "Budget and timeline constraints?", blocking: true },
];

const DEMO_CONVERGENCE = {
  overall: 0.42,
  status: "in_progress" as const,
};

export default function TruthMapDrawer({ open, onOpenChange }: TruthMapDrawerProps) {
  return (
    <>
      {/* Mobile: bottom sheet */}
      <Sheet open={open} onOpenChange={onOpenChange}>
        <SheetContent
          side="bottom"
          className="h-[85dvh] rounded-t-2xl sm:hidden"
        >
          <SheetHeader>
            <SheetTitle className="flex items-center gap-2">
              Truth Map
              <ConvergenceBadge
                overall={DEMO_CONVERGENCE.overall}
                status={DEMO_CONVERGENCE.status}
              />
            </SheetTitle>
          </SheetHeader>
          <ScrollArea className="mt-4 h-[calc(85dvh-80px)]">
            <TruthMapContent />
          </ScrollArea>
        </SheetContent>
      </Sheet>

      {/* Desktop: sidebar */}
      {open && (
        <aside className="hidden w-[380px] shrink-0 border-l border-border/50 bg-card/30 sm:block lg:w-[420px]">
          <div className="flex items-center gap-2 border-b border-border/50 px-5 py-3">
            <h2 className="text-sm font-semibold">Truth Map</h2>
            <ConvergenceBadge
              overall={DEMO_CONVERGENCE.overall}
              status={DEMO_CONVERGENCE.status}
            />
          </div>
          <ScrollArea className="h-[calc(100dvh-120px)]">
            <TruthMapContent />
          </ScrollArea>
        </aside>
      )}
    </>
  );
}

function ConvergenceBadge({
  overall,
  status,
}: Readonly<{
  overall: number;
  status: string;
}>) {
  const percent = Math.round(overall * 100);
  const variant =
    status === "converged"
      ? "default"
      : percent >= 60
        ? "secondary"
        : "outline";

  return (
    <Badge variant={variant} className="ml-auto text-xs font-mono">
      {percent}% converged
    </Badge>
  );
}

function TruthMapContent() {
  return (
    <div className="flex flex-col gap-6 px-5 py-4">
      {/* Open Questions */}
      <TruthMapSection
        title="Open Questions"
        icon={<HelpCircle className="h-4 w-4 text-amber-500" />}
        count={DEMO_OPEN_QUESTIONS.length}
      >
        {DEMO_OPEN_QUESTIONS.map((q) => (
          <div
            key={q.id}
            className="flex items-start gap-2 rounded-lg bg-muted/40 px-3 py-2.5 text-sm"
          >
            <CircleDot className="mt-0.5 h-3.5 w-3.5 shrink-0 text-amber-500" />
            <span>{q.text}</span>
            {q.blocking && (
              <Badge variant="destructive" className="ml-auto shrink-0 text-[10px]">
                Blocking
              </Badge>
            )}
          </div>
        ))}
      </TruthMapSection>

      <Separator />

      {/* Claims */}
      <TruthMapSection
        title="Claims"
        icon={<Lightbulb className="h-4 w-4 text-blue-500" />}
        count={DEMO_CLAIMS.length}
      >
        {DEMO_CLAIMS.map((claim) => (
          <div
            key={claim.id}
            className={`rounded-lg border px-3 py-2.5 text-sm ${
              claim.status === "contested"
                ? "border-contested/30 bg-contested/5"
                : "border-border/30 bg-muted/30"
            }`}
          >
            <div className="flex items-center justify-between gap-2">
              <span className="text-xs text-muted-foreground">
                {claim.agent}
              </span>
              <ConfidencePill confidence={claim.confidence} />
            </div>
            <p className="mt-1.5 leading-relaxed">{claim.text}</p>
            {claim.status === "contested" && (
              <div className="mt-2 flex items-center gap-1.5 text-xs text-contested">
                <AlertTriangle className="h-3 w-3" />
                Contested — requires validation
              </div>
            )}
          </div>
        ))}
      </TruthMapSection>

      <Separator />

      {/* Risks */}
      <TruthMapSection
        title="Risks"
        icon={<ShieldAlert className="h-4 w-4 text-red-500" />}
        count={DEMO_RISKS.length}
      >
        {DEMO_RISKS.map((risk) => (
          <div
            key={risk.id}
            className="flex items-start gap-2 rounded-lg bg-muted/30 px-3 py-2.5 text-sm"
          >
            <SeverityDot severity={risk.severity} />
            <div>
              <p className="leading-relaxed">{risk.text}</p>
              <span className="text-xs text-muted-foreground">
                {risk.category} · {risk.severity}
              </span>
            </div>
          </div>
        ))}
      </TruthMapSection>

      <Separator />

      {/* Assumptions */}
      <TruthMapSection
        title="Assumptions"
        icon={<CheckCircle2 className="h-4 w-4 text-emerald-500" />}
        count={DEMO_ASSUMPTIONS.length}
      >
        {DEMO_ASSUMPTIONS.map((a) => (
          <div
            key={a.id}
            className="flex items-start gap-2 rounded-lg bg-muted/30 px-3 py-2.5 text-sm"
          >
            <Badge
              variant={a.status === "validated" ? "default" : "outline"}
              className="shrink-0 text-[10px]"
            >
              {a.status}
            </Badge>
            <p className="leading-relaxed">{a.text}</p>
          </div>
        ))}
      </TruthMapSection>
    </div>
  );
}

function TruthMapSection({
  title,
  icon,
  count,
  children,
}: Readonly<{
  title: string;
  icon: React.ReactNode;
  count: number;
  children: React.ReactNode;
}>) {
  return (
    <div>
      <div className="mb-3 flex items-center gap-2">
        {icon}
        <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
          {title}
        </h3>
        <span className="ml-auto font-mono text-xs text-muted-foreground">
          {count}
        </span>
      </div>
      <div className="flex flex-col gap-2">{children}</div>
    </div>
  );
}

function ConfidencePill({ confidence }: Readonly<{ confidence: number }>) {
  const percent = Math.round(confidence * 100);
  const color =
    confidence < 0.3
      ? "text-contested"
      : confidence < 0.6
        ? "text-amber-500"
        : "text-emerald-500";

  return (
    <span className={`font-mono text-xs ${color}`}>{percent}%</span>
  );
}

function SeverityDot({ severity }: Readonly<{ severity: string }>) {
  const color =
    severity === "critical"
      ? "bg-red-500"
      : severity === "high"
        ? "bg-orange-500"
        : severity === "medium"
          ? "bg-amber-500"
          : "bg-emerald-500";

  return (
    <span className={`mt-1.5 h-2 w-2 shrink-0 rounded-full ${color}`} />
  );
}
