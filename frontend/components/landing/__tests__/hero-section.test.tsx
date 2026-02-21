import { describe, it, expect } from "vitest";
import { render, screen } from "@/lib/test-utils";
import HeroSection from "@/components/landing/hero-section";

describe("HeroSection", () => {
  it("renders 'Agon' as part of the main headline", () => {
    render(<HeroSection />);
    const heading = screen.getByRole("heading", { level: 1 });
    expect(heading).toHaveTextContent("Agon");
  });

  it("renders the tagline 'Stress-test your ideas before reality does' in the main headline", () => {
    render(<HeroSection />);
    const heading = screen.getByRole("heading", { level: 1 });
    expect(heading).toHaveTextContent(/stress-test your ideas/i);
    expect(heading).toHaveTextContent(/before reality does/i);
  });

  it("renders the product description paragraph", () => {
    render(<HeroSection />);
    expect(
      screen.getByText(/council of specialist ai agents/i),
    ).toBeInTheDocument();
  });

  it("renders a link to start a session", () => {
    render(<HeroSection />);
    const startLink = screen.getByRole("link", { name: /start a session/i });
    expect(startLink).toHaveAttribute("href", "/session/new");
  });

  it("renders a link to view past sessions", () => {
    render(<HeroSection />);
    const sessionsLink = screen.getByRole("link", {
      name: /view past sessions/i,
    });
    expect(sessionsLink).toHaveAttribute("href", "/sessions");
  });

  it("renders all four feature cards", () => {
    render(<HeroSection />);
    expect(screen.getByText("Multi-Model Council")).toBeInTheDocument();
    expect(screen.getByText("Red-Team Built In")).toBeInTheDocument();
    expect(screen.getByText("Living Truth Map")).toBeInTheDocument();
    expect(screen.getByText("Decision-Grade Output")).toBeInTheDocument();
  });

  it("does not render a 'Living Strategy Room' pill", () => {
    render(<HeroSection />);
    expect(screen.queryByText("Living Strategy Room")).not.toBeInTheDocument();
  });
});
