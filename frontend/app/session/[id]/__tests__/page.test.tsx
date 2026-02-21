import { describe, it, expect } from "vitest";
import { render, screen } from "@/lib/test-utils";
import userEvent from "@testing-library/user-event";
import SessionPage from "@/app/session/[id]/page";

describe("SessionPage", () => {
  it("renders the session header with Agon brand", () => {
    render(<SessionPage />);
    expect(screen.getByText("Agon")).toBeInTheDocument();
  });

  it("renders the thread view with demo messages", () => {
    render(<SessionPage />);
    expect(
      screen.getByText(/session started/i),
    ).toBeInTheDocument();
  });

  it("renders the message composer", () => {
    render(<SessionPage />);
    expect(
      screen.getByPlaceholderText(/answer the clarifying questions/i),
    ).toBeInTheDocument();
  });

  it("starts with the Truth Map drawer closed", () => {
    render(<SessionPage />);
    // When closed, the Truth Map panel title should not be visible
    // (it only appears in the sheet/sidebar when open)
    expect(screen.queryByText("Truth Map")).not.toBeInTheDocument();
  });

  it("opens the Truth Map drawer when toggle is clicked", async () => {
    const user = userEvent.setup();
    render(<SessionPage />);

    // Find the Map toggle — it's the last button in the header row
    const headerButtons = screen
      .getAllByRole("button")
      .filter((btn) => btn.closest("header"));
    const lastHeaderButton = headerButtons[headerButtons.length - 1];
    await user.click(lastHeaderButton);

    const truthMapHeadings = screen.getAllByText("Truth Map");
    expect(truthMapHeadings.length).toBeGreaterThanOrEqual(1);
  });

  it("displays the CLARIFICATION phase label", () => {
    render(<SessionPage />);
    expect(screen.getByText("Clarifying")).toBeInTheDocument();
  });

  it("starts with friction level at 50", () => {
    render(<SessionPage />);
    expect(screen.getByText("50")).toBeInTheDocument();
  });
});
