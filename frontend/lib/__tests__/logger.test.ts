import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { type Logger, LogLevel, createLogger } from "@/lib/logger";

describe("createLogger", () => {
  let warnSpy: ReturnType<typeof vi.spyOn>;
  let errorSpy: ReturnType<typeof vi.spyOn>;
  let infoSpy: ReturnType<typeof vi.spyOn>;
  let debugSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});
    errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    infoSpy = vi.spyOn(console, "info").mockImplementation(() => {});
    debugSpy = vi.spyOn(console, "debug").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("creates a logger with the given component name", () => {
    const logger = createLogger("TestComponent");
    expect(logger).toBeDefined();
    expect(logger.info).toBeTypeOf("function");
    expect(logger.warn).toBeTypeOf("function");
    expect(logger.error).toBeTypeOf("function");
    expect(logger.debug).toBeTypeOf("function");
  });

  it("includes the component name in log output", () => {
    const logger = createLogger("SessionHeader", LogLevel.DEBUG);
    logger.info("mounted");
    expect(infoSpy).toHaveBeenCalledWith(
      expect.stringContaining("[SessionHeader]"),
      expect.stringContaining("mounted"),
    );
  });

  it("logs info messages with context data", () => {
    const logger = createLogger("Composer", LogLevel.DEBUG);
    logger.info("message sent", { sessionId: "abc-123" });
    expect(infoSpy).toHaveBeenCalledWith(
      expect.stringContaining("[Composer]"),
      expect.stringContaining("message sent"),
      expect.objectContaining({ sessionId: "abc-123" }),
    );
  });

  it("logs warn messages", () => {
    const logger = createLogger("TruthMap", LogLevel.DEBUG);
    logger.warn("claim contested", { claimId: "c1" });
    expect(warnSpy).toHaveBeenCalledWith(
      expect.stringContaining("[TruthMap]"),
      expect.stringContaining("claim contested"),
      expect.objectContaining({ claimId: "c1" }),
    );
  });

  it("logs error messages with error objects", () => {
    const logger = createLogger("API", LogLevel.DEBUG);
    const err = new Error("Network failure");
    logger.error("request failed", { endpoint: "/sessions" }, err);
    expect(errorSpy).toHaveBeenCalledWith(
      expect.stringContaining("[API]"),
      expect.stringContaining("request failed"),
      expect.objectContaining({ endpoint: "/sessions" }),
      err,
    );
  });

  it("logs debug messages when level is DEBUG", () => {
    const logger = createLogger("Thread", LogLevel.DEBUG);
    logger.debug("scroll position", { offset: 42 });
    expect(debugSpy).toHaveBeenCalledWith(
      expect.stringContaining("[Thread]"),
      expect.stringContaining("scroll position"),
      expect.objectContaining({ offset: 42 }),
    );
  });

  it("suppresses debug messages when level is INFO", () => {
    const logger = createLogger("Thread", LogLevel.INFO);
    logger.debug("should not appear");
    expect(debugSpy).not.toHaveBeenCalled();
  });

  it("suppresses info and debug messages when level is WARN", () => {
    const logger = createLogger("Thread", LogLevel.WARN);
    logger.debug("hidden");
    logger.info("also hidden");
    expect(debugSpy).not.toHaveBeenCalled();
    expect(infoSpy).not.toHaveBeenCalled();
  });

  it("suppresses all but error when level is ERROR", () => {
    const logger = createLogger("Thread", LogLevel.ERROR);
    logger.debug("hidden");
    logger.info("hidden");
    logger.warn("hidden");
    logger.error("visible");
    expect(debugSpy).not.toHaveBeenCalled();
    expect(infoSpy).not.toHaveBeenCalled();
    expect(warnSpy).not.toHaveBeenCalled();
    expect(errorSpy).toHaveBeenCalled();
  });

  it("suppresses all messages when level is SILENT", () => {
    const logger = createLogger("Thread", LogLevel.SILENT);
    logger.debug("hidden");
    logger.info("hidden");
    logger.warn("hidden");
    logger.error("hidden");
    expect(debugSpy).not.toHaveBeenCalled();
    expect(infoSpy).not.toHaveBeenCalled();
    expect(warnSpy).not.toHaveBeenCalled();
    expect(errorSpy).not.toHaveBeenCalled();
  });

  it("omits context object from output when none is provided", () => {
    const logger = createLogger("Nav", LogLevel.DEBUG);
    logger.info("navigated");
    expect(infoSpy).toHaveBeenCalledWith(
      expect.stringContaining("[Nav]"),
      expect.stringContaining("navigated"),
    );
    // Should NOT have a third argument
    expect(infoSpy.mock.calls[0]).toHaveLength(2);
  });
});

describe("Logger type", () => {
  it("satisfies the Logger interface", () => {
    const logger: Logger = createLogger("TypeCheck");
    expect(logger.info).toBeTypeOf("function");
    expect(logger.warn).toBeTypeOf("function");
    expect(logger.error).toBeTypeOf("function");
    expect(logger.debug).toBeTypeOf("function");
  });
});
