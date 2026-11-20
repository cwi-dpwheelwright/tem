import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useRef, useState } from "react";
import { useGlobalInputFocus } from "./useGlobalInputFocus";

interface HarnessProps {
  enabled?: boolean;
  modalOpen?: boolean;
  onSubmit?: () => void;
}

function installMatchMedia(coarsePointer = false, hoverNone = coarsePointer) {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: (coarsePointer && query.includes("pointer: coarse")) || (hoverNone && query.includes("hover: none")),
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
}

function Harness({ enabled = true, modalOpen = false, onSubmit = vi.fn() }: HarnessProps) {
  const [value, setValue] = useState("");
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const globalInputFocus = useGlobalInputFocus({
    inputRef,
    value,
    onValueChange: setValue,
    onSubmit,
    enabled,
    isModalOpen: modalOpen,
    clearOnEscape: true,
  });

  return (
    <>
      <p data-testid="page-copy">Selectable page copy</p>
      <textarea
        ref={inputRef}
        aria-label="Ask CWI"
        value={value}
        onChange={(event) => setValue(event.target.value)}
        onKeyDown={globalInputFocus.onKeyDown}
      />
      <input aria-label="Other input" />
      <button type="button">Other button</button>
    </>
  );
}

describe("useGlobalInputFocus", () => {
  beforeEach(() => {
    installMatchMedia(false);
    Object.defineProperty(window, "requestAnimationFrame", {
      writable: true,
      value: vi.fn((callback: FrameRequestCallback) => {
        callback(0);
        return 0;
      }),
    });
  });

  afterEach(() => {
    cleanup();
    window.getSelection()?.removeAllRanges();
    vi.restoreAllMocks();
  });

  it("focuses the main input on page load", async () => {
    render(<Harness />);

    const input = screen.getByLabelText("Ask CWI");
    await waitFor(() => expect(document.activeElement).toBe(input));
  });

  it("preserves the first typed character when typing outside the input", async () => {
    render(<Harness />);

    const input = screen.getByLabelText("Ask CWI") as HTMLTextAreaElement;
    input.blur();
    fireEvent.keyDown(window, { key: "h" });

    expect(document.activeElement).toBe(input);
    expect(input.value).toBe("h");
  });

  it("refocuses after clicking a non-editable page area", () => {
    render(<Harness />);

    const input = screen.getByLabelText("Ask CWI") as HTMLTextAreaElement;
    const pageCopy = screen.getByTestId("page-copy");
    pageCopy.click();
    input.blur();

    fireEvent.keyDown(window, { key: "w" });

    expect(document.activeElement).toBe(input);
    expect(input.value).toBe("w");
  });

  it("uses slash as a focus shortcut without inserting slash", () => {
    render(<Harness />);

    const input = screen.getByLabelText("Ask CWI") as HTMLTextAreaElement;
    input.blur();
    fireEvent.keyDown(window, { key: "/" });

    expect(document.activeElement).toBe(input);
    expect(input.value).toBe("");
  });

  it("does not hijack modal typing", () => {
    render(<Harness modalOpen />);

    const input = screen.getByLabelText("Ask CWI") as HTMLTextAreaElement;
    fireEvent.keyDown(window, { key: "h" });

    expect(document.activeElement).not.toBe(input);
    expect(input.value).toBe("");
  });

  it("does not hijack another editable field", () => {
    render(<Harness />);

    const input = screen.getByLabelText("Ask CWI") as HTMLTextAreaElement;
    const otherInput = screen.getByLabelText("Other input") as HTMLInputElement;
    otherInput.focus();
    fireEvent.keyDown(otherInput, { key: "h" });
    fireEvent.change(otherInput, { target: { value: "h" } });

    expect(document.activeElement).toBe(otherInput);
    expect(otherInput.value).toBe("h");
    expect(input.value).toBe("");
  });

  it("submits on Enter and preserves Shift Enter for newline input", () => {
    const onSubmit = vi.fn();
    render(<Harness onSubmit={onSubmit} />);

    const input = screen.getByLabelText("Ask CWI") as HTMLTextAreaElement;
    fireEvent.change(input, { target: { value: "hello" } });
    expect(fireEvent.keyDown(input, { key: "Enter", shiftKey: true })).toBe(true);

    fireEvent.keyDown(input, { key: "Enter" });
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  it("does not intercept browser or app shortcuts", () => {
    render(<Harness />);

    const input = screen.getByLabelText("Ask CWI") as HTMLTextAreaElement;
    input.blur();
    fireEvent.keyDown(window, { key: "f", ctrlKey: true });
    fireEvent.keyDown(window, { key: "r", metaKey: true });
    fireEvent.keyDown(window, { key: "x", altKey: true });

    expect(document.activeElement).not.toBe(input);
    expect(input.value).toBe("");
  });

  it("does not hijack typing while page text is selected", () => {
    render(<Harness />);

    const input = screen.getByLabelText("Ask CWI") as HTMLTextAreaElement;
    const otherButton = screen.getByRole("button", { name: "Other button" });
    const pageCopy = screen.getByTestId("page-copy");
    const range = document.createRange();
    range.selectNodeContents(pageCopy);
    window.getSelection()?.removeAllRanges();
    window.getSelection()?.addRange(range);
    vi.spyOn(window, "getSelection").mockReturnValue({
      isCollapsed: false,
      toString: () => "Selectable page copy",
      removeAllRanges: vi.fn(),
      addRange: vi.fn(),
    } as unknown as Selection);
    otherButton.focus();

    fireEvent.keyDown(window, { key: "h" });

    expect(document.activeElement).toBe(otherButton);
    expect(input.value).toBe("");
  });

  it("stays disabled on coarse pointer devices", () => {
    installMatchMedia(true);
    render(<Harness />);

    const input = screen.getByLabelText("Ask CWI") as HTMLTextAreaElement;
    expect(document.activeElement).not.toBe(input);

    fireEvent.keyDown(window, { key: "h" });
    expect(document.activeElement).not.toBe(input);
    expect(input.value).toBe("");
  });

  it("stays enabled when hover is unavailable but pointer is not coarse", () => {
    installMatchMedia(false, true);
    render(<Harness />);

    const input = screen.getByLabelText("Ask CWI") as HTMLTextAreaElement;
    input.blur();
    fireEvent.keyDown(window, { key: "h" });

    expect(document.activeElement).toBe(input);
    expect(input.value).toBe("h");
  });
});
