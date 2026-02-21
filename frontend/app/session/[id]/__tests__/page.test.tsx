import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@/lib/test-utils";
import userEvent from "@testing-library/user-event";

const { useParamsMock } = vi.hoisted(() => ({
  useParamsMock: vi.fn(() => ({ id: "session-123" })),
}));
const fetchMock = vi.fn();

vi.mock("next/navigation", async () => {
  const actual = await vi.importActual<typeof import("next/navigation")>(
    "next/navigation",
  );

  return {
    ...actual,
    useParams: useParamsMock,
  };
});

import SessionPage from "@/app/session/[id]/page";

describe("SessionPage", () => {
  beforeEach(() => {
    fetchMock.mockReset();
    useParamsMock.mockReturnValue({ id: "session-123" });
    vi.stubGlobal("fetch", fetchMock);

    fetchMock
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            sessionId: "session-123",
            phase: "DebateRound1",
            frictionLevel: 72,
            status: "Active",
          }),
          { status: 200, headers: { "content-type": "application/json" } },
        ),
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            sessionId: "session-123",
            coreIdea: "Backend-connected session",
            version: 0,
            round: 1,
          }),
          { status: 200, headers: { "content-type": "application/json" } },
        ),
      );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders the session header with Agon brand", async () => {
    render(<SessionPage />);
    await waitFor(() => {
      expect(screen.getByText("Agon")).toBeInTheDocument();
    });
  });

  it("renders the thread view with demo messages", async () => {
    render(<SessionPage />);
    await waitFor(() => {
      expect(
        screen.getByText(/session started/i),
      ).toBeInTheDocument();
    });
  });

  it("renders the message composer", async () => {
    render(<SessionPage />);
    await waitFor(() => {
      expect(
        screen.getByPlaceholderText(/challenge a claim/i),
      ).toBeInTheDocument();
    });
  });

  it("starts with the Truth Map drawer closed", async () => {
    render(<SessionPage />);
    await waitFor(() => {
      // When closed, the Truth Map panel title should not be visible
      // (it only appears in the sheet/sidebar when open)
      expect(screen.queryByText("Truth Map")).not.toBeInTheDocument();
    });
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

  it("displays the mapped phase label from backend session state", async () => {
    render(<SessionPage />);
    await waitFor(() => {
      expect(screen.getByText("Round 1 — Divergence")).toBeInTheDocument();
    });
  });

  it("loads friction level from backend session state", async () => {
    render(<SessionPage />);
    await waitFor(() => {
      expect(screen.getByText("72")).toBeInTheDocument();
    });
  });

  it("calls session and truthmap endpoints for the route session id", async () => {
    render(<SessionPage />);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenNthCalledWith(1, "/sessions/session-123");
      expect(fetchMock).toHaveBeenNthCalledWith(2, "/sessions/session-123/truthmap");
    });
  });
});
