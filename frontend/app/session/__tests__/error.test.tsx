import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@/lib/test-utils";
import SessionError from "@/app/session/[id]/error";

describe("SessionError (app/session/[id]/error.tsx)", () => {
  it("renders a session-specific error message", () => {
    const reset = vi.fn();
    render(<SessionError error={new Error("fail")} reset={reset} />);
    expect(screen.getByText("Session error")).toBeInTheDocument();
  });

  it("includes a retry button that calls reset", () => {
    const reset = vi.fn();
    render(<SessionError error={new Error("fail")} reset={reset} />);

    const btn = screen.getByRole("button", { name: /try again/i });
    btn.click();
    expect(reset).toHaveBeenCalledTimes(1);
  });

  it("includes a link to start a new session", () => {
    const reset = vi.fn();
    render(<SessionError error={new Error("fail")} reset={reset} />);

    const link = screen.getByRole("link", { name: /new session|start over/i });
    expect(link).toHaveAttribute("href", "/session/new");
  });

  it("does not expose raw error details", () => {
    const reset = vi.fn();
    render(
      <SessionError
        error={new Error("SIGNALR_DISCONNECT: ws://10.0.0.1")}
        reset={reset}
      />,
    );
    expect(screen.queryByText(/SIGNALR_DISCONNECT/)).not.toBeInTheDocument();
  });
});
