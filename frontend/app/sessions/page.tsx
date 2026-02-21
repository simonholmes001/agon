import Link from "next/link";
import { ArrowLeft, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";

// Demo data — will be replaced by GET /sessions
const DEMO_SESSIONS = [
  {
    id: "demo",
    idea: "A mobile app that helps freelancers manage invoices and track payments",
    status: "active",
    phase: "CLARIFICATION",
    frictionLevel: 50,
    createdAt: "2026-02-19T10:00:00Z",
  },
];

export default function SessionsPage() {
  return (
    <div className="flex min-h-[100dvh] flex-col bg-background">
      <header className="flex items-center gap-3 border-b border-border/50 px-4 py-3 sm:px-6">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <h1 className="flex-1 text-sm font-medium text-muted-foreground">
          Sessions
        </h1>
        <Button size="sm" className="gap-2" asChild>
          <Link href="/session/new">
            <Plus className="h-3.5 w-3.5" />
            New
          </Link>
        </Button>
      </header>

      <main className="mx-auto w-full max-w-3xl px-4 py-6 sm:px-6">
        {DEMO_SESSIONS.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-24 text-center">
            <p className="text-lg font-medium">No sessions yet</p>
            <p className="mt-1 text-sm text-muted-foreground">
              Start your first session to begin analysing an idea.
            </p>
            <Button className="mt-6 gap-2" asChild>
              <Link href="/session/new">
                <Plus className="h-4 w-4" />
                Start a Session
              </Link>
            </Button>
          </div>
        ) : (
          <div className="flex flex-col gap-3">
            {DEMO_SESSIONS.map((session) => (
              <Link
                key={session.id}
                href={`/session/${session.id}`}
                className="group rounded-xl border border-border/40 bg-card/60 p-4 transition-colors hover:border-border hover:bg-card sm:p-5"
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="flex-1">
                    <p className="font-medium leading-snug group-hover:text-primary">
                      {session.idea}
                    </p>
                    <div className="mt-2 flex flex-wrap items-center gap-2">
                      <Badge variant="secondary" className="text-xs">
                        {session.phase}
                      </Badge>
                      <span className="text-xs text-muted-foreground">
                        Friction: {session.frictionLevel}
                      </span>
                      <span className="text-xs text-muted-foreground">
                        {new Date(session.createdAt).toLocaleDateString()}
                      </span>
                    </div>
                  </div>
                  <StatusDot status={session.status} />
                </div>
              </Link>
            ))}
          </div>
        )}
      </main>
    </div>
  );
}

function StatusDot({ status }: Readonly<{ status: string }>) {
  const color =
    status === "active"
      ? "bg-emerald-500"
      : status === "complete"
        ? "bg-blue-500"
        : "bg-muted-foreground";

  return (
    <span className="mt-1.5 flex items-center gap-1.5">
      <span className={`h-2 w-2 rounded-full ${color}`} />
      <span className="text-xs capitalize text-muted-foreground">{status}</span>
    </span>
  );
}
