import React from "react";
import { render, type RenderOptions } from "@testing-library/react";
import { TooltipProvider } from "@/components/ui/tooltip";

function AllProviders({ children }: Readonly<{ children: React.ReactNode }>) {
  return <TooltipProvider>{children}</TooltipProvider>;
}

function renderWithProviders(
  ui: React.ReactElement,
  options?: Omit<RenderOptions, "wrapper">,
) {
  return render(ui, { wrapper: AllProviders, ...options });
}

export { renderWithProviders as render };
export { screen, within, waitFor } from "@testing-library/react";
