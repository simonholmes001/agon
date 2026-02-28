"use client";

import { useState, useCallback } from "react";
import { Download, FileText, Check, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { createLogger } from "@/lib/logger";

const logger = createLogger("ExportArtifactsDialog");

const BACKEND_API_PREFIX = "/api/backend";

export interface ArtifactType {
  id: string;
  label: string;
  description: string;
}

const AVAILABLE_ARTIFACTS: ArtifactType[] = [
  {
    id: "copilot",
    label: "Copilot Instructions",
    description: "GitHub Copilot configuration for your project",
  },
  {
    id: "architecture",
    label: "Architecture",
    description: "Technical architecture documentation",
  },
  {
    id: "prd",
    label: "PRD",
    description: "Product Requirements Document",
  },
  {
    id: "risks",
    label: "Risk Registry",
    description: "Identified risks with mitigations",
  },
  {
    id: "assumptions",
    label: "Assumptions",
    description: "Assumption validation table",
  },
];

interface ExportArtifactsDialogProps {
  readonly sessionId: string;
  readonly disabled?: boolean;
}

export default function ExportArtifactsDialog({
  sessionId,
  disabled = false,
}: ExportArtifactsDialogProps) {
  const [open, setOpen] = useState(false);
  const [selectedTypes, setSelectedTypes] = useState<Set<string>>(
    new Set(AVAILABLE_ARTIFACTS.map((a) => a.id))
  );
  const [isExporting, setIsExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const toggleType = useCallback((typeId: string) => {
    setSelectedTypes((prev) => {
      const next = new Set(prev);
      if (next.has(typeId)) {
        next.delete(typeId);
      } else {
        next.add(typeId);
      }
      return next;
    });
  }, []);

  const selectAll = useCallback(() => {
    setSelectedTypes(new Set(AVAILABLE_ARTIFACTS.map((a) => a.id)));
  }, []);

  const selectNone = useCallback(() => {
    setSelectedTypes(new Set());
  }, []);

  const handleExport = useCallback(async () => {
    if (selectedTypes.size === 0) {
      setError("Please select at least one artifact type");
      return;
    }

    setIsExporting(true);
    setError(null);

    try {
      logger.info("Exporting artifacts", {
        sessionId,
        types: Array.from(selectedTypes),
      });

      const response = await fetch(
        `${BACKEND_API_PREFIX}/sessions/${sessionId}/artifacts/export`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            types: Array.from(selectedTypes),
          }),
        }
      );

      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`Export failed: ${response.status} - ${errorText}`);
      }

      // Get the blob and trigger download
      const blob = await response.blob();
      const url = globalThis.URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;

      // Extract filename from Content-Disposition header if available
      const contentDisposition = response.headers.get("Content-Disposition");
      let filename = `agon-artifacts-${sessionId}.zip`;
      if (contentDisposition) {
        const filenameRegex = /filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/;
        const filenameMatch = filenameRegex.exec(contentDisposition);
        if (filenameMatch?.[1]) {
          filename = filenameMatch[1].replaceAll(/['"]/g, "");
        }
      }

      link.download = filename;
      document.body.appendChild(link);
      link.click();
      link.remove();
      globalThis.URL.revokeObjectURL(url);

      logger.info("Artifacts exported successfully", { sessionId, filename });
      setOpen(false);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Export failed";
      logger.error("Export failed", { sessionId, error: message });
      setError(message);
    } finally {
      setIsExporting(false);
    }
  }, [sessionId, selectedTypes]);

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button
          variant="outline"
          size="sm"
          disabled={disabled}
          className="gap-2"
        >
          <Download className="h-4 w-4" />
          <span className="hidden sm:inline">Export</span>
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Export Artifacts</DialogTitle>
          <DialogDescription>
            Select the artifacts to include in your download. All artifacts are
            generated from your session&apos;s Truth Map.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4 py-4">
          {/* Selection controls */}
          <div className="flex items-center justify-between text-sm">
            <span className="text-muted-foreground">
              {selectedTypes.size} of {AVAILABLE_ARTIFACTS.length} selected
            </span>
            <div className="flex gap-2">
              <Button
                variant="ghost"
                size="sm"
                onClick={selectAll}
                className="h-7 text-xs"
              >
                Select all
              </Button>
              <Button
                variant="ghost"
                size="sm"
                onClick={selectNone}
                className="h-7 text-xs"
              >
                Clear
              </Button>
            </div>
          </div>

          {/* Artifact list */}
          <div className="space-y-2">
            {AVAILABLE_ARTIFACTS.map((artifact) => (
              <button
                key={artifact.id}
                type="button"
                onClick={() => toggleType(artifact.id)}
                className={`flex w-full items-start gap-3 rounded-lg border p-3 text-left transition-colors ${
                  selectedTypes.has(artifact.id)
                    ? "border-primary bg-primary/5"
                    : "border-border hover:border-muted-foreground/50"
                }`}
              >
                <div
                  className={`mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded border ${
                    selectedTypes.has(artifact.id)
                      ? "border-primary bg-primary text-primary-foreground"
                      : "border-muted-foreground/30"
                  }`}
                >
                  {selectedTypes.has(artifact.id) && (
                    <Check className="h-3 w-3" />
                  )}
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <FileText className="h-4 w-4 text-muted-foreground" />
                    <span className="font-medium text-sm">{artifact.label}</span>
                  </div>
                  <p className="text-xs text-muted-foreground mt-0.5">
                    {artifact.description}
                  </p>
                </div>
              </button>
            ))}
          </div>

          {/* Error message */}
          {error && (
            <div className="rounded-md bg-destructive/10 px-3 py-2 text-sm text-destructive">
              {error}
            </div>
          )}
        </div>

        {/* Actions */}
        <div className="flex justify-end gap-2">
          <Button
            variant="outline"
            onClick={() => setOpen(false)}
            disabled={isExporting}
          >
            Cancel
          </Button>
          <Button
            onClick={handleExport}
            disabled={isExporting || selectedTypes.size === 0}
            className="gap-2"
          >
            {isExporting ? (
              <>
                <Loader2 className="h-4 w-4 animate-spin" />
                Exporting...
              </>
            ) : (
              <>
                <Download className="h-4 w-4" />
                Download ZIP
              </>
            )}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
