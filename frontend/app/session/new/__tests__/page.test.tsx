import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@/lib/test-utils";
import userEvent from "@testing-library/user-event";

const pushMock = vi.fn();

// Mock next/navigation before importing the component
vi.mock("next/navigation", () => ({
  useRouter: vi.fn(() => ({
    push: pushMock,
    replace: vi.fn(),
    back: vi.fn(),
    prefetch: vi.fn(),
    refresh: vi.fn(),
  })),
  usePathname: vi.fn(() => "/session/new"),
}));

import NewSessionPage from "@/app/session/new/page";

describe("NewSessionPage", () => {
  beforeEach(() => {
    pushMock.mockClear();
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  // ── Rendering ───────────────────────────────────────────────────

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

  it("renders a back link to home", () => {
    render(<NewSessionPage />);
    const backLink = screen.getByRole("link");
    expect(backLink).toHaveAttribute("href", "/");
  });

  it("shows Brainstorm, Balanced, and Adversarial labels on slider", () => {
    render(<NewSessionPage />);
    expect(screen.getByText("Brainstorm")).toBeInTheDocument();
    expect(screen.getAllByText("Balanced").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText("Adversarial")).toBeInTheDocument();
  });

  // ── Character count ─────────────────────────────────────────────

  it("shows 'at least 10 characters' when idea is empty", () => {
    render(<NewSessionPage />);
    expect(screen.getByText("At least 10 characters (0/10)")).toBeInTheDocument();
  });

  it("shows character count progress when typing under 10 characters", async () => {
    render(<NewSessionPage />);
    const textarea = screen.getByPlaceholderText(/mobile app/i);
    await userEvent.type(textarea, "Short");
    expect(screen.getByText("At least 10 characters (5/10)")).toBeInTheDocument();
  });

  it("shows total character count once the minimum is met", async () => {
    render(<NewSessionPage />);
    const textarea = screen.getByPlaceholderText(/mobile app/i);
    await userEvent.type(textarea, "This idea is long enough");
    expect(screen.getByText(/24 characters/)).toBeInTheDocument();
  });

  // ── Submit button state ─────────────────────────────────────────

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

  // ── Form submission ─────────────────────────────────────────────

  it("does not submit when the idea is too short", async () => {
    render(<NewSessionPage />);
    const textarea = screen.getByPlaceholderText(/mobile app/i);
    await userEvent.type(textarea, "Short");

    // Force submit via Enter or form action
    const button = screen.getByRole("button", { name: /launch council/i });
    // Button should be disabled, so clicking it should do nothing
    await userEvent.click(button);

    expect(pushMock).not.toHaveBeenCalled();
  });

  it("shows a submitting state after clicking Launch Council", async () => {
    render(<NewSessionPage />);
    const textarea = screen.getByPlaceholderText(/mobile app/i);
    await userEvent.type(textarea, "A marketplace for handmade crafts targeting artisan sellers");

    const button = screen.getByRole("button", { name: /launch council/i });
    await userEvent.click(button);

    // After clicking, the button text changes to "Starting session…"
    expect(screen.getByText(/starting session/i)).toBeInTheDocument();
    // And the button is disabled to prevent double-submit
    expect(screen.getByRole("button", { name: /starting session/i })).toBeDisabled();
  });

  it("navigates to the demo session after submission", async () => {
    render(<NewSessionPage />);
    const textarea = screen.getByPlaceholderText(/mobile app/i);
    await userEvent.type(textarea, "A marketplace for handmade crafts targeting artisan sellers");

    const button = screen.getByRole("button", { name: /launch council/i });
    await userEvent.click(button);

    // The setTimeout fires after 800ms
    vi.advanceTimersByTime(800);

    await waitFor(() => {
      expect(pushMock).toHaveBeenCalledWith("/session/demo");
    });
  });

  it("does not navigate if idea is only whitespace padded to 10+ chars", async () => {
    render(<NewSessionPage />);
    const textarea = screen.getByPlaceholderText(/mobile app/i);
    // Type mostly whitespace — trimmed length < 10
    await userEvent.type(textarea, "   ab     ");

    const button = screen.getByRole("button", { name: /launch council/i });
    // Button should be disabled because trimmed length is 2
    expect(button).toBeDisabled();
    await userEvent.click(button);

    vi.advanceTimersByTime(1000);
    expect(pushMock).not.toHaveBeenCalled();
  });
});
