import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@/lib/test-utils";
import GlobalError from "@/app/global-error";

describe("GlobalError", () => {
  it("renders a user-friendly error message", () => {
    const error = new Error("Something broke");
    const reset = vi.fn();
    // global-error receives error and reset as props
    render(<GlobalError error={error} reset={reset} />);

    expect(screen.getByText("Something went wrong")).toBeInTheDocument();
    expect(
      screen.getByText(/unexpected error occurred/i),
    ).toBeInTheDocument();
  });

  it("renders a retry button that calls reset", () => {
    const error = new Error("Kaboom");
    const reset = vi.fn();
    render(<GlobalError error={error} reset={reset} />);

    const retryButton = screen.getByRole("button", { name: /try again/i });
    expect(retryButton).toBeInTheDocument();
    retryButton.click();
    expect(reset).toHaveBeenCalledTimes(1);
  });

  it("renders a link to return home", () => {
    const error = new Error("Fatal");
    const reset = vi.fn();
    render(<GlobalError error={error} reset={reset} />);

    const homeLink = screen.getByRole("link", { name: /home/i });
    expect(homeLink).toBeInTheDocument();
    expect(homeLink).toHaveAttribute("href", "/");
  });

  it("does not expose the raw error message to the user", () => {
    const error = new Error("DB_CONNECTION_REFUSED: 10.0.0.1:5432");
    const reset = vi.fn();
    render(<GlobalError error={error} reset={reset} />);

    expect(
      screen.queryByText(/DB_CONNECTION_REFUSED/),
    ).not.toBeInTheDocument();
  });
});
