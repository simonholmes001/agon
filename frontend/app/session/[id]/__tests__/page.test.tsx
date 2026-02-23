import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@/lib/test-utils";
import { act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const {
  useParamsMock,
  useSearchParamsMock,
  createDebateHubConnectionMock,
  startMock,
  stopMock,
  onRoundProgressMock,
  onTruthMapPatchMock,
  onTranscriptMessageMock,
  onReconnectedMock,
} = vi.hoisted(() => ({
  useParamsMock: vi.fn(() => ({ id: "session-123" })),
  useSearchParamsMock: vi.fn(() => new URLSearchParams()),
  startMock: vi.fn(() => Promise.resolve(true)),
  stopMock: vi.fn(() => Promise.resolve()),
  onRoundProgressMock: vi.fn(),
  onTruthMapPatchMock: vi.fn(),
  onTranscriptMessageMock: vi.fn(),
  onReconnectedMock: vi.fn(),
  createDebateHubConnectionMock: vi.fn(),
}));
const fetchMock = vi.fn();

createDebateHubConnectionMock.mockImplementation(() => ({
  start: startMock,
  stop: stopMock,
  onRoundProgress: onRoundProgressMock,
  onTruthMapPatch: onTruthMapPatchMock,
  onTranscriptMessage: onTranscriptMessageMock,
  onReconnected: onReconnectedMock,
}));

vi.mock("next/navigation", async () => {
  const actual = await vi.importActual<typeof import("next/navigation")>(
    "next/navigation",
  );

  return {
    ...actual,
    useParams: useParamsMock,
    useSearchParams: useSearchParamsMock,
  };
});

vi.mock("@/lib/realtime/debate-hub", () => ({
  createDebateHubConnection: createDebateHubConnectionMock,
}));

import SessionPage from "@/app/session/[id]/page";

describe("SessionPage", () => {
  beforeEach(() => {
    fetchMock.mockReset();
    createDebateHubConnectionMock.mockClear();
    startMock.mockClear();
    stopMock.mockClear();
    onRoundProgressMock.mockClear();
    onTruthMapPatchMock.mockClear();
    onTranscriptMessageMock.mockClear();
    onReconnectedMock.mockClear();
    useParamsMock.mockReturnValue({ id: "session-123" });
    useSearchParamsMock.mockReturnValue(new URLSearchParams());
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
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify([
            {
              id: "msg-1",
              type: "agent",
              agentId: "synthesis-validation",
              round: 1,
              isStreaming: false,
              content: "Moderator summary content.",
            },
          ]),
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

  it("renders the thread view with backend session context", async () => {
    render(<SessionPage />);
    await waitFor(() => {
      expect(
        screen.getByText(/session session-123 loaded from backend/i),
      ).toBeInTheDocument();
    });
  });

  it("renders transcript messages returned by the backend", async () => {
    render(<SessionPage />);
    await waitFor(() => {
      expect(
        screen.getByText(/moderator summary content/i),
      ).toBeInTheDocument();
    });
  });

  it("renders the message composer", async () => {
    render(<SessionPage />);
    await waitFor(() => {
      expect(
        screen.getByPlaceholderText(/message the council moderator/i),
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
      expect(screen.getAllByText("Round 1 — Divergence").length).toBeGreaterThanOrEqual(1);
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
      expect(fetchMock).toHaveBeenNthCalledWith(1, "/api/backend/sessions/session-123");
      expect(fetchMock).toHaveBeenNthCalledWith(2, "/api/backend/sessions/session-123/truthmap");
      expect(fetchMock).toHaveBeenNthCalledWith(3, "/api/backend/sessions/session-123/transcript");
    });
  });

  it("connects to the SignalR debate hub for the route session id", async () => {
    render(<SessionPage />);

    await waitFor(() => {
      expect(createDebateHubConnectionMock).toHaveBeenCalledWith("session-123");
      expect(startMock).toHaveBeenCalledTimes(1);
      expect(onRoundProgressMock).toHaveBeenCalledTimes(1);
      expect(onTruthMapPatchMock).toHaveBeenCalledTimes(1);
      expect(onTranscriptMessageMock).toHaveBeenCalledTimes(1);
      expect(onReconnectedMock).toHaveBeenCalledTimes(1);
    });
  });

  it("updates phase label when a RoundProgress event arrives", async () => {
    render(<SessionPage />);

    await waitFor(() => {
      expect(onRoundProgressMock).toHaveBeenCalledTimes(1);
    });

    const handler = onRoundProgressMock.mock.calls[0]?.[0] as
      | ((event: { phase: string }) => void)
      | undefined;
    expect(handler).toBeTypeOf("function");

    act(() => {
      handler?.({ phase: "DebateRound2" });
    });

    expect(screen.getAllByText("Round 2 — Crossfire").length).toBeGreaterThanOrEqual(1);
  });

  it("re-fetches session and truth map on hub reconnect", async () => {
    fetchMock.mockReset();
    fetchMock.mockImplementation((input: string | URL | Request) => {
      const url = typeof input === "string" ? input : input.toString();
      if (url.includes("/truthmap")) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              sessionId: "session-123",
              coreIdea: "Backend-connected session",
              version: 2,
              round: 2,
            }),
            { status: 200, headers: { "content-type": "application/json" } },
          ),
        );
      }

      if (url.includes("/transcript")) {
        return Promise.resolve(
          new Response(
            JSON.stringify([
              {
                id: "msg-1",
                type: "agent",
                agentId: "socratic-clarifier",
                round: 1,
                isStreaming: false,
                content: "I have reviewed your core idea and will challenge assumptions.",
              },
            ]),
            { status: 200, headers: { "content-type": "application/json" } },
          ),
        );
      }

      return Promise.resolve(
        new Response(
          JSON.stringify({
            sessionId: "session-123",
            phase: "DebateRound1",
            frictionLevel: 72,
            status: "Active",
          }),
          { status: 200, headers: { "content-type": "application/json" } },
        ),
      );
    });

    render(<SessionPage />);

    await waitFor(() => {
      expect(onReconnectedMock).toHaveBeenCalledTimes(1);
    });

    const reconnectedHandler = onReconnectedMock.mock.calls[0]?.[0] as
      | (() => void)
      | undefined;
    expect(reconnectedHandler).toBeTypeOf("function");

    await act(async () => {
      reconnectedHandler?.();
    });

    await waitFor(() => {
      expect(fetchMock).toHaveBeenNthCalledWith(4, "/api/backend/sessions/session-123");
      expect(fetchMock).toHaveBeenNthCalledWith(5, "/api/backend/sessions/session-123/truthmap");
      expect(fetchMock).toHaveBeenNthCalledWith(6, "/api/backend/sessions/session-123/transcript");
    });
  });

  it("shows realtime unavailable state when signalr startup fails", async () => {
    startMock.mockResolvedValueOnce(false);
    render(<SessionPage />);

    await waitFor(() => {
      expect(
        screen.getByText(/real-time updates unavailable/i),
      ).toBeInTheDocument();
    });
  });

  it("appends transcript messages pushed from SignalR", async () => {
    render(<SessionPage />);

    await waitFor(() => {
      expect(onTranscriptMessageMock).toHaveBeenCalledTimes(1);
    });

    const transcriptHandler = onTranscriptMessageMock.mock.calls[0]?.[0] as
      | ((event: {
        id: string;
        type: string;
        content: string;
        agentId?: string;
        round?: number;
        isStreaming?: boolean;
        createdAtUtc?: string;
      }) => void)
      | undefined;
    expect(transcriptHandler).toBeTypeOf("function");

    act(() => {
      transcriptHandler?.({
        id: "msg-live-1",
        type: "agent",
        agentId: "synthesis-validation",
        content: "Moderator streaming update from the backend.",
        round: 1,
        isStreaming: true,
        createdAtUtc: "2026-02-22T00:00:00Z",
      });
    });

    expect(
      screen.getByText(/moderator streaming update from the backend/i),
    ).toBeInTheDocument();
  });

  it("auto-starts the council run when start=1 is present in query params", async () => {
    useSearchParamsMock.mockReturnValue(new URLSearchParams("start=1"));
    fetchMock.mockReset();

    fetchMock
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            sessionId: "session-123",
            phase: "Clarification",
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
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify([]), {
          status: 200,
          headers: { "content-type": "application/json" },
        }),
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            sessionId: "session-123",
            phase: "DebateRound1",
          }),
          { status: 200, headers: { "content-type": "application/json" } },
        ),
      );

    render(<SessionPage />);

    await waitFor(() => {
      expect(fetchMock).toHaveBeenNthCalledWith(
        4,
        "/api/backend/sessions/session-123/start",
        expect.objectContaining({ method: "POST" }),
      );
    });
  });

  it("posts moderator messages through the backend endpoint", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          sessionId: "session-123",
          phase: "PostDelivery",
          routedAgentId: "product_strategist",
          reply: "Moderator acknowledged your follow-up.",
          patchApplied: false,
        }),
        { status: 200, headers: { "content-type": "application/json" } },
      ),
    );

    render(<SessionPage />);

    const input = await screen.findByPlaceholderText(/message the council moderator/i);
    await user.type(input, "Challenge the weakest point in this recommendation.");
    await user.keyboard("{Enter}");

    await waitFor(() => {
      expect(fetchMock).toHaveBeenNthCalledWith(
        4,
        "/api/backend/sessions/session-123/messages",
        expect.objectContaining({
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            message: "Challenge the weakest point in this recommendation.",
          }),
        }),
      );
    });
  });
});
