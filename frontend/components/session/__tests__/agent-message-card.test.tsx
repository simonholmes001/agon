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

  it("shows moderator summary label when variant is moderatorSummary", () => {
    render(
      <AgentMessageCard
        {...defaultProps}
        agentId="synthesis-validation"
        variant="moderatorSummary"
      />,
    );
    expect(screen.getByText(/moderator summary/i)).toBeInTheDocument();
  });

  it("renders Markdown bold text as HTML strong elements", () => {
    const { container } = render(
      <AgentMessageCard
        {...defaultProps}
        content="This has **bold text** in it."
      />,
    );
    const strong = container.querySelector("strong");
    expect(strong).toBeInTheDocument();
    expect(strong).toHaveTextContent("bold text");
  });

  it("does not render raw Markdown symbols in the output", () => {
    render(
      <AgentMessageCard
        {...defaultProps}
        content="**Here are my questions:**"
      />,
    );
    expect(screen.getByText("Here are my questions:")).toBeInTheDocument();
    expect(screen.queryByText(/\*\*/)).not.toBeInTheDocument();
  });

  it("renders Markdown ordered lists as HTML list elements", () => {
    const { container } = render(
      <AgentMessageCard
        {...defaultProps}
        content={"1. First item\n2. Second item\n3. Third item"}
      />,
    );
    const listItems = container.querySelectorAll("li");
    expect(listItems).toHaveLength(3);
    expect(listItems[0]).toHaveTextContent("First item");
    expect(listItems[1]).toHaveTextContent("Second item");
    expect(listItems[2]).toHaveTextContent("Third item");
  });

  it("renders inline Markdown within list items", () => {
    const { container } = render(
      <AgentMessageCard
        {...defaultProps}
        content={"1. **Who is your user?** Some details here."}
      />,
    );
    const strong = container.querySelector("li strong");
    expect(strong).toBeInTheDocument();
    expect(strong).toHaveTextContent("Who is your user?");
  });
});
