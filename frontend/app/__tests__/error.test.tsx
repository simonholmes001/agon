import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@/lib/test-utils";
import RootError from "@/app/error";

describe("RootError (app/error.tsx)", () => {
  it("renders a user-friendly error heading", () => {
    const reset = vi.fn();
    render(<RootError error={new Error("fail")} reset={reset} />);
    expect(screen.getByText("Something went wrong")).toBeInTheDocument();
  });

  it("renders a retry button that calls reset", () => {
    const reset = vi.fn();
    render(<RootError error={new Error("fail")} reset={reset} />);

    const btn = screen.getByRole("button", { name: /try again/i });
    btn.click();
    expect(reset).toHaveBeenCalledTimes(1);
  });

  it("renders a link to return home", () => {
    const reset = vi.fn();
    render(<RootError error={new Error("fail")} reset={reset} />);

    const link = screen.getByRole("link", { name: /home/i });
    expect(link).toHaveAttribute("href", "/");
  });

  it("does not expose the raw error message", () => {
    const reset = vi.fn();
    render(
      <RootError error={new Error("SECRET_TOKEN_abc123")} reset={reset} />,
    );
    expect(screen.queryByText(/SECRET_TOKEN/)).not.toBeInTheDocument();
  });
});
