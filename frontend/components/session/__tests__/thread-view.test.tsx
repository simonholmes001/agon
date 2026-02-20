import { describe, it, expect } from "vitest";
import { render, screen } from "@/lib/test-utils";
import ThreadView from "@/components/session/thread-view";

describe("ThreadView", () => {
  it("renders the system message", () => {
    render(<ThreadView sessionId="test-session" />);
    expect(
      screen.getByText(
        /session started. the socratic clarifier is reviewing your idea/i,
      ),
    ).toBeInTheDocument();
  });

  it("renders the Socratic Clarifier agent message card", () => {
    render(<ThreadView sessionId="test-session" />);
    expect(screen.getByText("Socratic Clarifier")).toBeInTheDocument();
  });

  it("renders agent message content as Markdown", () => {
    const { container } = render(<ThreadView sessionId="test-session" />);
    // The content contains **bold** markers — they should be rendered as <strong>
    const strongElements = container.querySelectorAll("strong");
    expect(strongElements.length).toBeGreaterThan(0);
  });

  it("renders all demo messages", () => {
    render(<ThreadView sessionId="test-session" />);
    // 1 system message + 1 agent message = 2 messages total
    expect(
      screen.getByText(
        /session started/i,
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/i've reviewed your idea/i),
    ).toBeInTheDocument();
  });

  it("displays the system message centered", () => {
    render(<ThreadView sessionId="test-session" />);
    const systemMessage = screen.getByText(
      /session started/i,
    );
    // System message is inside a centered container
    expect(systemMessage.closest(".flex.justify-center")).toBeInTheDocument();
  });
});
