"use client";

import Link from "next/link";
import { Button } from "@/components/ui/button";
import { createLogger } from "@/lib/logger";

const logger = createLogger("SessionError");

interface SessionErrorProps {
  readonly error: Error & { digest?: string };
  readonly reset: () => void;
}

export default function SessionError({ error, reset }: SessionErrorProps) {
  logger.error("Session error", { digest: error.digest }, error);

  return (
    <div className="flex min-h-[60vh] flex-col items-center justify-center gap-6 p-4 text-center">
      <div className="space-y-2">
        <h1 className="text-2xl font-bold tracking-tight">Session error</h1>
        <p className="text-sm text-muted-foreground">
          Something went wrong with this session. You can retry or start a
          new one.
        </p>
      </div>
      <div className="flex gap-3">
        <Button onClick={reset} variant="default">
          Try again
        </Button>
        <Button variant="outline" asChild>
          <Link href="/session/new">Start over</Link>
        </Button>
      </div>
    </div>
  );
}
