"use client";

import { useState } from "react";
import SessionHeader from "@/components/session/session-header";
import ThreadView from "@/components/session/thread-view";
import TruthMapDrawer from "@/components/session/truth-map-drawer";
import MessageComposer from "@/components/session/message-composer";
import type { SessionPhase } from "@/types";

// Demo data — will be replaced by real API + SignalR state
const DEMO_SESSION = {
  id: "demo",
  phase: "CLARIFICATION" as SessionPhase,
  frictionLevel: 50,
  round: 0,
};

export default function SessionPage() {
  const [truthMapOpen, setTruthMapOpen] = useState(false);
  const [frictionLevel, setFrictionLevel] = useState(
    DEMO_SESSION.frictionLevel,
  );

  return (
    <div className="flex h-[100dvh] flex-col bg-background">
      <SessionHeader
        sessionId={DEMO_SESSION.id}
        phase={DEMO_SESSION.phase}
        frictionLevel={frictionLevel}
        onFrictionChange={setFrictionLevel}
        onToggleTruthMap={() => setTruthMapOpen(!truthMapOpen)}
        truthMapOpen={truthMapOpen}
      />

      <div className="relative flex flex-1 overflow-hidden">
        {/* Thread — always visible */}
        <ThreadView sessionId={DEMO_SESSION.id} />

        {/* Truth Map — drawer on mobile, sidebar on desktop */}
        <TruthMapDrawer
          open={truthMapOpen}
          onOpenChange={setTruthMapOpen}
        />
      </div>

      <MessageComposer
        sessionId={DEMO_SESSION.id}
        phase={DEMO_SESSION.phase}
      />
    </div>
  );
}
