import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@/lib/test-utils";
import userEvent from "@testing-library/user-event";

const pushMock = vi.fn();
const fetchMock = vi.fn();

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
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
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

  it("renders a theme toggle control in the header", () => {
    render(<NewSessionPage />);
    expect(screen.getByRole("button", { name: /toggle theme/i })).toBeInTheDocument();
  });

  it("renders a persistent launch action bar", () => {
    render(<NewSessionPage />);
    expect(screen.getByTestId("launch-action-bar")).toBeInTheDocument();
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
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("shows a submitting state after clicking Launch Council", async () => {
    fetchMock
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            sessionId: "session-1",
            phase: "Clarification",
            frictionLevel: 50,
          }),
          { status: 201, headers: { "content-type": "application/json" } },
        ),
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            sessionId: "session-1",
            phase: "DebateRound1",
            frictionLevel: 50,
          }),
          { status: 200, headers: { "content-type": "application/json" } },
        ),
      );

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

  it("calls backend create/start endpoints and navigates to the created session", async () => {
    fetchMock
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            sessionId: "session-42",
            phase: "Clarification",
            frictionLevel: 50,
          }),
          { status: 201, headers: { "content-type": "application/json" } },
        ),
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            sessionId: "session-42",
            phase: "DebateRound1",
            frictionLevel: 50,
          }),
          { status: 200, headers: { "content-type": "application/json" } },
        ),
      );

    render(<NewSessionPage />);
    const textarea = screen.getByPlaceholderText(/mobile app/i);
    await userEvent.type(textarea, "A marketplace for handmade crafts targeting artisan sellers");

    const button = screen.getByRole("button", { name: /launch council/i });
    await userEvent.click(button);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenNthCalledWith(
        1,
        "/api/backend/sessions",
        expect.objectContaining({
          method: "POST",
          headers: expect.objectContaining({
            "Content-Type": "application/json",
          }),
        }),
      );
      expect(fetchMock).toHaveBeenNthCalledWith(
        2,
        "/api/backend/sessions/session-42/start",
        expect.objectContaining({ method: "POST" }),
      );
      expect(pushMock).toHaveBeenCalledWith("/session/session-42");
    });
  });

  it("submits and starts session when Enter is pressed in the idea textarea", async () => {
    fetchMock
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            sessionId: "session-88",
            phase: "Clarification",
            frictionLevel: 50,
          }),
          { status: 201, headers: { "content-type": "application/json" } },
        ),
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            sessionId: "session-88",
            phase: "DebateRound1",
            frictionLevel: 50,
          }),
          { status: 200, headers: { "content-type": "application/json" } },
        ),
      );

    render(<NewSessionPage />);
    const textarea = screen.getByPlaceholderText(/mobile app/i);
    await userEvent.type(
      textarea,
      "An app that helps fans follow Kovi's releases and events{enter}",
    );

    await waitFor(() => {
      expect(fetchMock).toHaveBeenNthCalledWith(
        1,
        "/api/backend/sessions",
        expect.objectContaining({ method: "POST" }),
      );
      expect(fetchMock).toHaveBeenNthCalledWith(
        2,
        "/api/backend/sessions/session-88/start",
        expect.objectContaining({ method: "POST" }),
      );
      expect(pushMock).toHaveBeenCalledWith("/session/session-88");
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

    expect(pushMock).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
