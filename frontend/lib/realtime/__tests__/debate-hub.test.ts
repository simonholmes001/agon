import { describe, it, expect } from "vitest";
import { createDebateHubConnection } from "@/lib/realtime/debate-hub";

describe("createDebateHubConnection", () => {
  it("surfaces startup errors so callers can handle them with logging/fallback", async () => {
    const connection = createDebateHubConnection("session-abc");

    connection.onRoundProgress(() => {});
    connection.onTruthMapPatch(() => {});
    connection.onReconnected(() => {});

    await expect(connection.start()).rejects.toBeTruthy();
    await expect(connection.stop()).resolves.toBeUndefined();
  });
});
