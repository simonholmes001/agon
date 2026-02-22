import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@/lib/test-utils";
import userEvent from "@testing-library/user-event";
import MessageComposer from "@/components/session/message-composer";

describe("MessageComposer", () => {
  const defaultProps = {
    sessionId: "test-session",
    phase: "CLARIFICATION" as const,
  };

  it("renders a text input", () => {
    render(<MessageComposer {...defaultProps} />);
    expect(
      screen.getByPlaceholderText(/answer the clarifying questions/i),
    ).toBeInTheDocument();
  });

  it("renders a send button", () => {
    render(<MessageComposer {...defaultProps} />);
    const sendButton = screen.getByRole("button");
    expect(sendButton).toBeInTheDocument();
  });

  it("send button is disabled when input is empty", () => {
    render(<MessageComposer {...defaultProps} />);
    const sendButton = screen.getByRole("button");
    expect(sendButton).toBeDisabled();
  });

  it("send button is enabled when input has content", async () => {
    render(<MessageComposer {...defaultProps} />);
    const textarea = screen.getByPlaceholderText(
      /answer the clarifying questions/i,
    );
    await userEvent.type(textarea, "My target user is solo freelancers");
    const sendButton = screen.getByRole("button");
    expect(sendButton).toBeEnabled();
  });

  it("shows phase-appropriate placeholder for CLARIFICATION", () => {
    render(<MessageComposer {...defaultProps} phase="CLARIFICATION" />);
    expect(
      screen.getByPlaceholderText(/answer the clarifying questions/i),
    ).toBeInTheDocument();
  });

  it("shows phase-appropriate placeholder for DEBATE_ROUND_1", () => {
    render(<MessageComposer {...defaultProps} phase="DEBATE_ROUND_1" />);
    expect(
      screen.getByPlaceholderText(/message the council moderator/i),
    ).toBeInTheDocument();
  });

  it("shows phase-appropriate placeholder for POST_DELIVERY", () => {
    render(<MessageComposer {...defaultProps} phase="POST_DELIVERY" />);
    expect(
      screen.getByPlaceholderText(/council moderator/i),
    ).toBeInTheDocument();
  });

  it("is disabled during SYNTHESIS phase", () => {
    render(<MessageComposer {...defaultProps} phase="SYNTHESIS" />);
    const textarea = screen.getByPlaceholderText(
      /the council is synthesising/i,
    );
    expect(textarea).toBeDisabled();
  });

  it("is disabled during TARGETED_LOOP phase", () => {
    render(<MessageComposer {...defaultProps} phase="TARGETED_LOOP" />);
    expect(
      screen.getByPlaceholderText(/the council is synthesising/i),
    ).toBeDisabled();
  });

  it("is disabled during DELIVER phase", () => {
    render(<MessageComposer {...defaultProps} phase="DELIVER" />);
    const textarea = screen.getByRole("textbox");
    expect(textarea).toBeDisabled();
  });

  it("clears input after submit", async () => {
    render(<MessageComposer {...defaultProps} />);
    const textarea = screen.getByPlaceholderText(
      /answer the clarifying questions/i,
    );
    await userEvent.type(textarea, "My budget is $50k");
    await userEvent.keyboard("{Enter}");
    expect(textarea).toHaveValue("");
  });

  it("submits to moderator callback when provided", async () => {
    const onSubmitMessage = vi.fn<(...args: [string]) => Promise<void>>()
      .mockResolvedValue(undefined);

    render(
      <MessageComposer
        {...defaultProps}
        phase="POST_DELIVERY"
        onSubmitMessage={onSubmitMessage}
      />,
    );

    const textarea = screen.getByPlaceholderText(/council moderator/i);
    await userEvent.type(textarea, "Please challenge the weakest assumption.");
    await userEvent.keyboard("{Enter}");

    await waitFor(() => {
      expect(onSubmitMessage).toHaveBeenCalledWith(
        "Please challenge the weakest assumption.",
      );
    });
  });
});
