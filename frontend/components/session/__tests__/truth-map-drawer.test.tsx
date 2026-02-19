import { describe, it, expect } from "vitest";
import { render, screen } from "@/lib/test-utils";
import TruthMapDrawer from "@/components/session/truth-map-drawer";

// When open=true, both the Sheet (mobile) and aside (desktop) render
// TruthMapContent, so most text appears twice. Use getAllByText.

describe("TruthMapDrawer", () => {
  it("renders desktop sidebar with Truth Map title when open", () => {
    render(<TruthMapDrawer open={true} onOpenChange={() => {}} />);
    const titles = screen.getAllByText("Truth Map");
    expect(titles.length).toBeGreaterThanOrEqual(1);
  });

  it("does not render desktop sidebar when closed", () => {
    const { container } = render(
      <TruthMapDrawer open={false} onOpenChange={() => {}} />,
    );
    const aside = container.querySelector("aside");
    expect(aside).not.toBeInTheDocument();
  });

  it("displays convergence percentage", () => {
    render(<TruthMapDrawer open={true} onOpenChange={() => {}} />);
    const badges = screen.getAllByText(/42% converged/i);
    expect(badges.length).toBeGreaterThanOrEqual(1);
  });

  it("renders open questions section", () => {
    render(<TruthMapDrawer open={true} onOpenChange={() => {}} />);
    const headings = screen.getAllByText("Open Questions");
    expect(headings.length).toBeGreaterThanOrEqual(1);
  });

  it("renders claims section", () => {
    render(<TruthMapDrawer open={true} onOpenChange={() => {}} />);
    const headings = screen.getAllByText("Claims");
    expect(headings.length).toBeGreaterThanOrEqual(1);
  });

  it("renders risks section", () => {
    render(<TruthMapDrawer open={true} onOpenChange={() => {}} />);
    const headings = screen.getAllByText("Risks");
    expect(headings.length).toBeGreaterThanOrEqual(1);
  });

  it("renders assumptions section", () => {
    render(<TruthMapDrawer open={true} onOpenChange={() => {}} />);
    const headings = screen.getAllByText("Assumptions");
    expect(headings.length).toBeGreaterThanOrEqual(1);
  });

  it("shows blocking badge on blocking open questions", () => {
    render(<TruthMapDrawer open={true} onOpenChange={() => {}} />);
    const blockingBadges = screen.getAllByText("Blocking");
    expect(blockingBadges.length).toBeGreaterThan(0);
  });

  it("shows contested claim with warning indicator", () => {
    render(<TruthMapDrawer open={true} onOpenChange={() => {}} />);
    const warnings = screen.getAllByText(/contested — requires validation/i);
    expect(warnings.length).toBeGreaterThanOrEqual(1);
  });

  it("displays confidence percentages for claims", () => {
    render(<TruthMapDrawer open={true} onOpenChange={() => {}} />);
    expect(screen.getAllByText("82%").length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText("71%").length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText("24%").length).toBeGreaterThanOrEqual(1);
  });
});
