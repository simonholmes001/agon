import { describe, it, expect } from "vitest";
import { render, screen } from "@/lib/test-utils";
import userEvent from "@testing-library/user-event";
import ThemeToggle from "@/components/theme-toggle";

describe("ThemeToggle", () => {
  it("renders a toggle button", () => {
    render(<ThemeToggle />);
    expect(
      screen.getByRole("button", { name: /toggle theme/i }),
    ).toBeInTheDocument();
  });

  it("shows sr-only accessible label", () => {
    render(<ThemeToggle />);
    expect(screen.getByText("Toggle theme")).toBeInTheDocument();
  });

  it("displays Sun icon in dark mode by default", () => {
    const { container } = render(<ThemeToggle />);
    // Sun icon rendered in dark mode (lucide-sun class)
    expect(container.querySelector(".lucide-sun")).toBeInTheDocument();
    expect(container.querySelector(".lucide-moon")).not.toBeInTheDocument();
  });

  it("switches to Moon icon after toggling to light mode", async () => {
    const user = userEvent.setup();
    const { container } = render(<ThemeToggle />);

    const button = screen.getByRole("button", { name: /toggle theme/i });
    await user.click(button);

    expect(container.querySelector(".lucide-moon")).toBeInTheDocument();
    expect(container.querySelector(".lucide-sun")).not.toBeInTheDocument();
  });

  it("toggles back to Sun icon when clicked twice", async () => {
    const user = userEvent.setup();
    const { container } = render(<ThemeToggle />);

    const button = screen.getByRole("button", { name: /toggle theme/i });
    await user.click(button);
    await user.click(button);

    expect(container.querySelector(".lucide-sun")).toBeInTheDocument();
    expect(container.querySelector(".lucide-moon")).not.toBeInTheDocument();
  });
});
