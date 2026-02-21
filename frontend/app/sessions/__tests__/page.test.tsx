import { describe, it, expect } from "vitest";
import { render, screen } from "@/lib/test-utils";
import SessionsPage from "@/app/sessions/page";

describe("SessionsPage", () => {
  it("renders the Sessions heading", () => {
    render(<SessionsPage />);
    expect(screen.getByText("Sessions")).toBeInTheDocument();
  });

  it("renders a back link to home", () => {
    render(<SessionsPage />);
    const backLink = screen.getByRole("link", { name: "" });
    expect(backLink).toHaveAttribute("href", "/");
  });

  it("renders a New session link", () => {
    render(<SessionsPage />);
    const newLink = screen.getByRole("link", { name: /new/i });
    expect(newLink).toHaveAttribute("href", "/session/new");
  });

  it("renders the demo session idea text", () => {
    render(<SessionsPage />);
    expect(
      screen.getByText(
        /a mobile app that helps freelancers manage invoices/i,
      ),
    ).toBeInTheDocument();
  });

  it("renders the demo session phase badge", () => {
    render(<SessionsPage />);
    expect(screen.getByText("CLARIFICATION")).toBeInTheDocument();
  });

  it("renders the demo session friction level", () => {
    render(<SessionsPage />);
    expect(screen.getByText(/friction: 50/i)).toBeInTheDocument();
  });

  it("renders a status dot with the session status", () => {
    render(<SessionsPage />);
    expect(screen.getByText("active")).toBeInTheDocument();
  });

  it("links each session to its detail page", () => {
    render(<SessionsPage />);
    const sessionLink = screen.getByRole("link", {
      name: /a mobile app that helps freelancers/i,
    });
    expect(sessionLink).toHaveAttribute("href", "/session/demo");
  });
});
