import { describe, it, expect } from "vitest";
import { render, screen } from "@/lib/test-utils";
import AgentMessageCard from "@/components/session/agent-message-card";

describe("AgentMessageCard", () => {
  const defaultProps = {
    agentId: "socratic-clarifier" as const,
    content: "This is a test message from the Socratic Clarifier.",
    isStreaming: false,
    round: 1,
  };

  it("renders the agent name", () => {
    render(<AgentMessageCard {...defaultProps} />);
    expect(screen.getByText("Socratic Clarifier")).toBeInTheDocument();
  });

  it("renders the agent role description", () => {
    render(<AgentMessageCard {...defaultProps} />);
    expect(
      screen.getByText(/clarifies intent, constraints, and success metrics/i),
    ).toBeInTheDocument();
  });

  it("renders the message content", () => {
    render(<AgentMessageCard {...defaultProps} />);
    expect(
      screen.getByText("This is a test message from the Socratic Clarifier."),
    ).toBeInTheDocument();
  });

  it("renders the model badge on desktop", () => {
    render(<AgentMessageCard {...defaultProps} />);
    expect(screen.getByText("GPT-5.2 Thinking")).toBeInTheDocument();
  });

  it("shows streaming indicator when isStreaming is true", () => {
    render(<AgentMessageCard {...defaultProps} isStreaming={true} />);
    expect(screen.getByText("Thinking…")).toBeInTheDocument();
  });

  it("does not show streaming indicator when isStreaming is false", () => {
    render(<AgentMessageCard {...defaultProps} isStreaming={false} />);
    expect(screen.queryByText("Thinking…")).not.toBeInTheDocument();
  });

  it("shows contested warning icon when isContested is true", () => {
    const { container } = render(
      <AgentMessageCard {...defaultProps} isContested={true} />,
    );
    // The AlertTriangle SVG icon is rendered when contested
    const alertIcon = container.querySelector(".text-contested");
    expect(alertIcon).toBeInTheDocument();
  });

  it("does not show contested warning icon by default", () => {
    const { container } = render(<AgentMessageCard {...defaultProps} />);
    const alertIcon = container.querySelector(".text-contested");
    expect(alertIcon).not.toBeInTheDocument();
  });

  it("renders contextual actions menu button", () => {
    render(<AgentMessageCard {...defaultProps} />);
    // The MoreHorizontal button exists in the DOM (opacity-0 on non-hover)
    const buttons = screen.getAllByRole("button");
    expect(buttons.length).toBeGreaterThanOrEqual(1);
  });

  it("renders correctly for each agent type", () => {
    const agents = [
      "framing-challenger",
      "product-strategist",
      "technical-architect",
      "contrarian",
      "research-librarian",
      "synthesis-validation",
    ] as const;

    for (const agentId of agents) {
      const { unmount } = render(
        <AgentMessageCard
          {...defaultProps}
          agentId={agentId}
          content={`Message from ${agentId}`}
        />,
      );
      expect(screen.getByText(`Message from ${agentId}`)).toBeInTheDocument();
      unmount();
    }
  });
});
