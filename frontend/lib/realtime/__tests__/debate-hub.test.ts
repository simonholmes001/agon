import { describe, it, expect } from "vitest";
import {
  createDebateHubConnection,
  resolveDebateHubUrl,
} from "@/lib/realtime/debate-hub";

describe("createDebateHubConnection", () => {
  it("surfaces startup errors so callers can handle them with logging/fallback", async () => {
    const connection = createDebateHubConnection("session-abc");

    connection.onRoundProgress(() => {});
    connection.onTruthMapPatch(() => {});
    connection.onReconnected(() => {});

    await expect(connection.start()).rejects.toBeTruthy();
    await expect(connection.stop()).resolves.toBeUndefined();
  });

  it("defaults to direct local backend hub url when env var is absent", () => {
    const previousValue = process.env.NEXT_PUBLIC_DEBATE_HUB_URL;
    delete process.env.NEXT_PUBLIC_DEBATE_HUB_URL;

    const hubUrl = resolveDebateHubUrl();

    expect(hubUrl).toBe("http://localhost:5000/hubs/debate");

    if (previousValue !== undefined) {
      process.env.NEXT_PUBLIC_DEBATE_HUB_URL = previousValue;
    }
  });
});
