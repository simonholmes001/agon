import { describe, it, expect } from "vitest";
import { render, screen } from "@/lib/test-utils";
import ThreadView from "@/components/session/thread-view";
import type { SessionPhase } from "@/types";

interface ThreadViewTestProps {
  sessionId: string;
  phase: SessionPhase;
  coreIdea: string;
  realtimeStatus: "connecting" | "connected" | "unavailable";
  messages: Array<{
    id: string;
    type: "agent" | "system";
    content: string;
    agentId?: "socratic-clarifier";
    round?: number;
    isStreaming?: boolean;
  }>;
}

const defaultProps: ThreadViewTestProps = {
  sessionId: "test-session",
  phase: "CLARIFICATION",
  coreIdea: "Build a SaaS for agile planning.",
  realtimeStatus: "connecting",
  messages: [],
};

describe("ThreadView", () => {
  it("renders backend session context banner", () => {
    render(<ThreadView {...defaultProps} phase="DEBATE_ROUND_1" />);
    expect(
      screen.getByText(/session test-session loaded from backend/i),
    ).toBeInTheDocument();
  });

  it("renders the core idea from backend truth map", () => {
    render(<ThreadView {...defaultProps} />);
    expect(
      screen.getByText(
        /core idea from backend/i,
      ),
    ).toBeInTheDocument();
    expect(screen.getByText(/build a saas for agile planning/i)).toBeInTheDocument();
  });

  it("renders realtime unavailable status when connection fails", () => {
    render(<ThreadView {...defaultProps} realtimeStatus="unavailable" />);
    expect(
      screen.getByText(/real-time updates unavailable/i),
    ).toBeInTheDocument();
  });

  it("renders realtime connected status", () => {
    render(
      <ThreadView
        {...defaultProps}
        phase="DEBATE_ROUND_1"
        realtimeStatus="connected"
      />,
    );
    expect(
      screen.getByText(/live council updates connected/i),
    ).toBeInTheDocument();
  });

  it("renders a backend waiting-state message instead of demo transcript", () => {
    render(<ThreadView {...defaultProps} phase="DEBATE_ROUND_1" />);
    expect(
      screen.getByText(
        /no agent transcript has been streamed yet/i,
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(/i've reviewed your idea/i)).not.toBeInTheDocument();
  });

  it("renders backend transcript messages when available", () => {
    render(
      <ThreadView
        {...defaultProps}
        messages={[
          {
            id: "agent-1",
            type: "agent",
            agentId: "socratic-clarifier",
            round: 1,
            isStreaming: false,
            content: "I have reviewed your core idea and will challenge assumptions.",
          },
        ]}
      />,
    );

    expect(
      screen.getByText(/i have reviewed your core idea/i),
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/no agent transcript has been streamed yet/i),
    ).not.toBeInTheDocument();
  });
});
