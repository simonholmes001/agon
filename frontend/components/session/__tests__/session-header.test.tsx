import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@/lib/test-utils";
import userEvent from "@testing-library/user-event";
import SessionHeader from "@/components/session/session-header";

describe("SessionHeader", () => {
  const defaultProps = {
    sessionId: "test-session-id",
    phase: "CLARIFICATION" as const,
    frictionLevel: 50,
    onFrictionChange: vi.fn(),
    onToggleTruthMap: vi.fn(),
    truthMapOpen: false,
  };

  it("renders the Agon brand name", () => {
    render(<SessionHeader {...defaultProps} />);
    expect(screen.getByText("Agon")).toBeInTheDocument();
  });

  it("displays the current phase label", () => {
    render(<SessionHeader {...defaultProps} />);
    expect(screen.getByText("Clarifying")).toBeInTheDocument();
  });

  it("displays phase label for debate round 1", () => {
    render(<SessionHeader {...defaultProps} phase="DEBATE_ROUND_1" />);
    expect(screen.getByText("Round 1 — Divergence")).toBeInTheDocument();
  });

  it("displays phase label for synthesis", () => {
    render(<SessionHeader {...defaultProps} phase="SYNTHESIS" />);
    expect(screen.getByText("Synthesising")).toBeInTheDocument();
  });

  it("renders a back link to home", () => {
    render(<SessionHeader {...defaultProps} />);
    const backLink = screen.getByRole("link");
    expect(backLink).toHaveAttribute("href", "/");
  });

  it("renders the Truth Map toggle button", async () => {
    const onToggle = vi.fn();
    render(
      <SessionHeader {...defaultProps} onToggleTruthMap={onToggle} />,
    );
    const toggleButtons = screen.getAllByRole("button");
    // The Map icon button is the last button in the header
    const mapButton = toggleButtons[toggleButtons.length - 1];
    await userEvent.click(mapButton);
    expect(onToggle).toHaveBeenCalledOnce();
  });

  it("shows friction level value", () => {
    render(<SessionHeader {...defaultProps} frictionLevel={75} />);
    // The friction value appears in the desktop slider area
    expect(screen.getByText("75")).toBeInTheDocument();
  });
});
