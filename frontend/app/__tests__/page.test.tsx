import { describe, it, expect } from "vitest";
import { render, screen } from "@/lib/test-utils";
import Home from "@/app/page";

describe("Home page", () => {
  it("renders a theme toggle button", () => {
    render(<Home />);
    expect(
      screen.getByRole("button", { name: /toggle theme/i }),
    ).toBeInTheDocument();
  });

  it("renders the hero section with the application name", () => {
    render(<Home />);
    expect(screen.getByText("Agon")).toBeInTheDocument();
  });
});
