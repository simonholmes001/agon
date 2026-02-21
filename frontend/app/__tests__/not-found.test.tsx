import { describe, expect, it } from "vitest";
import { render, screen } from "@/lib/test-utils";
import NotFound from "@/app/not-found";

describe("NotFound (app/not-found.tsx)", () => {
  it("renders a 404 heading", () => {
    render(<NotFound />);
    expect(screen.getByText("Page not found")).toBeInTheDocument();
  });

  it("renders a descriptive message", () => {
    render(<NotFound />);
    expect(
      screen.getByText(/doesn.t exist|could not be found/i),
    ).toBeInTheDocument();
  });

  it("renders a link back to home", () => {
    render(<NotFound />);
    const link = screen.getByRole("link", { name: /home/i });
    expect(link).toHaveAttribute("href", "/");
  });
});
