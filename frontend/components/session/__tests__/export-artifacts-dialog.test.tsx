import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { fireEvent } from "@testing-library/react";
import { render, screen, waitFor } from "@/lib/test-utils";
import ExportArtifactsDialog from "../export-artifacts-dialog";

// Mock fetch
const mockFetch = vi.fn();
globalThis.fetch = mockFetch;

// Mock URL methods
const mockCreateObjectURL = vi.fn(() => "blob:mock-url");
const mockRevokeObjectURL = vi.fn();
globalThis.URL.createObjectURL = mockCreateObjectURL;
globalThis.URL.revokeObjectURL = mockRevokeObjectURL;

describe("ExportArtifactsDialog", () => {
  const defaultProps = {
    sessionId: "test-session-123",
  };

  beforeEach(() => {
    vi.clearAllMocks();
    // Mock successful response by default
    mockFetch.mockResolvedValue({
      ok: true,
      blob: () => Promise.resolve(new Blob(["test"], { type: "application/zip" })),
      headers: new Headers({
        "Content-Disposition": 'attachment; filename="test-artifacts.zip"',
      }),
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders export button", () => {
    render(<ExportArtifactsDialog {...defaultProps} />);

    expect(screen.getByRole("button", { name: /export/i })).toBeInTheDocument();
  });

  it("disables export button when disabled prop is true", () => {
    render(<ExportArtifactsDialog {...defaultProps} disabled />);

    expect(screen.getByRole("button", { name: /export/i })).toBeDisabled();
  });

  it("opens dialog when export button is clicked", async () => {
    render(<ExportArtifactsDialog {...defaultProps} />);

    fireEvent.click(screen.getByRole("button", { name: /export/i }));

    await waitFor(() => {
      expect(screen.getByRole("dialog")).toBeInTheDocument();
    });
    expect(screen.getByText("Export Artifacts")).toBeInTheDocument();
  });

  it("shows all artifact types selected by default", async () => {
    render(<ExportArtifactsDialog {...defaultProps} />);

    fireEvent.click(screen.getByRole("button", { name: /export/i }));

    await waitFor(() => {
      expect(screen.getByText("Copilot Instructions")).toBeInTheDocument();
    });
    expect(screen.getByText("Architecture")).toBeInTheDocument();
    expect(screen.getByText("PRD")).toBeInTheDocument();
    expect(screen.getByText("Risk Registry")).toBeInTheDocument();
    expect(screen.getByText("Assumptions")).toBeInTheDocument();
    expect(screen.getByText("5 of 5 selected")).toBeInTheDocument();
  });

  it("allows toggling artifact selection", async () => {
    render(<ExportArtifactsDialog {...defaultProps} />);

    fireEvent.click(screen.getByRole("button", { name: /export/i }));

    await waitFor(() => {
      expect(screen.getByText("Copilot Instructions")).toBeInTheDocument();
    });

    // Click to deselect Copilot Instructions
    fireEvent.click(screen.getByText("Copilot Instructions"));

    await waitFor(() => {
      expect(screen.getByText("4 of 5 selected")).toBeInTheDocument();
    });
  });

  it("clears selection when Clear button is clicked", async () => {
    render(<ExportArtifactsDialog {...defaultProps} />);

    fireEvent.click(screen.getByRole("button", { name: /export/i }));

    await waitFor(() => {
      expect(screen.getByText("5 of 5 selected")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole("button", { name: /clear/i }));

    await waitFor(() => {
      expect(screen.getByText("0 of 5 selected")).toBeInTheDocument();
    });
  });

  it("selects all when Select all button is clicked", async () => {
    render(<ExportArtifactsDialog {...defaultProps} />);

    fireEvent.click(screen.getByRole("button", { name: /export/i }));

    await waitFor(() => {
      expect(screen.getByText("5 of 5 selected")).toBeInTheDocument();
    });

    // Clear first
    fireEvent.click(screen.getByRole("button", { name: /clear/i }));
    await waitFor(() => {
      expect(screen.getByText("0 of 5 selected")).toBeInTheDocument();
    });

    // Select all
    fireEvent.click(screen.getByRole("button", { name: /select all/i }));
    await waitFor(() => {
      expect(screen.getByText("5 of 5 selected")).toBeInTheDocument();
    });
  });

  it("disables download button when no artifacts selected", async () => {
    render(<ExportArtifactsDialog {...defaultProps} />);

    fireEvent.click(screen.getByRole("button", { name: /export/i }));

    await waitFor(() => {
      expect(screen.getByText("5 of 5 selected")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole("button", { name: /clear/i }));

    await waitFor(() => {
      expect(screen.getByRole("button", { name: /download zip/i })).toBeDisabled();
    });
  });

  it("calls API with selected types when downloading", async () => {
    render(<ExportArtifactsDialog {...defaultProps} />);

    fireEvent.click(screen.getByRole("button", { name: /export/i }));

    await waitFor(() => {
      expect(screen.getByText("5 of 5 selected")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole("button", { name: /download zip/i }));

    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith(
        `/api/backend/sessions/${defaultProps.sessionId}/artifacts/export`,
        expect.objectContaining({
          method: "POST",
          body: expect.stringContaining("copilot"),
        })
      );
    });
  });

  it("shows error message on API failure", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 500,
      text: () => Promise.resolve("Internal Server Error"),
    });

    render(<ExportArtifactsDialog {...defaultProps} />);

    fireEvent.click(screen.getByRole("button", { name: /export/i }));

    await waitFor(() => {
      expect(screen.getByText("5 of 5 selected")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole("button", { name: /download zip/i }));

    await waitFor(() => {
      expect(screen.getByText(/export failed/i)).toBeInTheDocument();
    });
  });

  it("closes dialog after successful export", async () => {
    // Create a proper mock link element
    const mockClick = vi.fn();
    const mockRemove = vi.fn();
    const originalCreateElement = document.createElement.bind(document);
    
    vi.spyOn(document, "createElement").mockImplementation((tagName: string) => {
      if (tagName === "a") {
        const element = originalCreateElement(tagName);
        element.click = mockClick;
        element.remove = mockRemove;
        return element;
      }
      return originalCreateElement(tagName);
    });

    render(<ExportArtifactsDialog {...defaultProps} />);

    fireEvent.click(screen.getByRole("button", { name: /export/i }));

    await waitFor(() => {
      expect(screen.getByRole("dialog")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole("button", { name: /download zip/i }));

    await waitFor(() => {
      expect(mockClick).toHaveBeenCalled();
    });

    // Dialog should close
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
  });

  it("shows loading state while exporting", async () => {
    // Make the fetch take longer
    mockFetch.mockImplementationOnce(
      () =>
        new Promise((resolve) =>
          setTimeout(
            () =>
              resolve({
                ok: true,
                blob: () => Promise.resolve(new Blob(["test"])),
                headers: new Headers(),
              }),
            100
          )
        )
    );

    render(<ExportArtifactsDialog {...defaultProps} />);

    fireEvent.click(screen.getByRole("button", { name: /export/i }));

    await waitFor(() => {
      expect(screen.getByText("5 of 5 selected")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole("button", { name: /download zip/i }));

    // Should show loading state immediately
    await waitFor(() => {
      expect(screen.getByText(/exporting/i)).toBeInTheDocument();
    });
  });
});
