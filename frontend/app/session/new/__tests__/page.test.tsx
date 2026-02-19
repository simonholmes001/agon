import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@/lib/test-utils";
import userEvent from "@testing-library/user-event";

// Mock next/navigation before importing the component
vi.mock("next/navigation", () => ({
  useRouter: vi.fn(() => ({
    push: vi.fn(),
    replace: vi.fn(),
    back: vi.fn(),
    prefetch: vi.fn(),
    refresh: vi.fn(),
  })),
  usePathname: vi.fn(() => "/session/new"),
}));

import NewSessionPage from "@/app/session/new/page";

describe("NewSessionPage", () => {
  it("renders the page heading", () => {
    render(<NewSessionPage />);
    expect(screen.getByText(/what's your idea/i)).toBeInTheDocument();
  });

  it("renders the idea textarea", () => {
    render(<NewSessionPage />);
    expect(
      screen.getByPlaceholderText(/mobile app/i),
    ).toBeInTheDocument();
  });

  it("renders the friction slider", () => {
    render(<NewSessionPage />);
    expect(screen.getByText(/friction level/i)).toBeInTheDocument();
  });

  it("shows friction label 'Balanced' at default value 50", () => {
    render(<NewSessionPage />);
    const balancedElements = screen.getAllByText("Balanced");
    expect(balancedElements.length).toBeGreaterThanOrEqual(1);
  });

  it("shows character count feedback", () => {
    render(<NewSessionPage />);
    expect(screen.getByText(/at least 10 characters/i)).toBeInTheDocument();
  });

  it("disables submit button when idea is too short", () => {
    render(<NewSessionPage />);
    const button = screen.getByRole("button", { name: /launch council/i });
    expect(button).toBeDisabled();
  });

  it("enables submit button when idea meets minimum length", async () => {
    render(<NewSessionPage />);
    const textarea = screen.getByPlaceholderText(/mobile app/i);
    await userEvent.type(textarea, "A tool for managing team retrospectives with AI-generated insights");
    const button = screen.getByRole("button", { name: /launch council/i });
    expect(button).toBeEnabled();
  });

  it("renders a back link to home", () => {
    render(<NewSessionPage />);
    const backLink = screen.getByRole("link", { name: "" });
    expect(backLink).toHaveAttribute("href", "/");
  });

  it("shows Brainstorm, Balanced, and Adversarial labels on slider", () => {
    render(<NewSessionPage />);
    expect(screen.getByText("Brainstorm")).toBeInTheDocument();
    // "Balanced" appears twice — once in the friction info and once in the label row
    expect(screen.getAllByText("Balanced").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText("Adversarial")).toBeInTheDocument();
  });
});
