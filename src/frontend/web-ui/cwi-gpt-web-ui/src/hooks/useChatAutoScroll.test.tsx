import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useEffect, useRef } from "react";
import { useChatAutoScroll } from "./useChatAutoScroll";

interface HarnessProps {
  enabled?: boolean;
  scrollKey: string;
  scrollTop?: number;
  scrollHeight?: number;
  clientHeight?: number;
}

function installMatchMedia() {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: query.includes("prefers-reduced-motion"),
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

function configureScrollableElement(
  element: HTMLElement,
  scrollTop: number,
  scrollHeight: number,
  clientHeight: number,
) {
  Object.defineProperty(element, "scrollHeight", {
    configurable: true,
    value: scrollHeight,
  });
  Object.defineProperty(element, "clientHeight", {
    configurable: true,
    value: clientHeight,
  });
  element.scrollTop = scrollTop;
  element.scrollTo = vi.fn((options?: ScrollToOptions | number, y?: number) => {
    if (typeof options === "number") {
      element.scrollTop = y ?? options;
      return;
    }
    element.scrollTop = Number(options?.top ?? element.scrollTop);
  });
}

function Harness({
  enabled = true,
  scrollKey,
  scrollTop = 800,
  scrollHeight = 1000,
  clientHeight = 200,
}: HarnessProps) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const element = scrollRef.current;
    if (!element) {
      return;
    }
    configureScrollableElement(element, scrollTop, scrollHeight, clientHeight);
  }, [clientHeight, scrollHeight, scrollTop]);

  const { isNearBottom, showJumpToBottom, scrollToLatest } = useChatAutoScroll(
    scrollRef,
    bottomRef,
    scrollKey,
    { enabled, bottomThresholdPx: 120, defaultBehavior: "smooth" },
  );

  return (
    <div>
      <div data-testid="state">{isNearBottom ? "near" : "away"}</div>
      <button
        type="button"
        aria-label="Jump to latest message"
        hidden={!showJumpToBottom}
        onClick={() => scrollToLatest("auto")}
      >
        Jump
      </button>
      <div ref={scrollRef} data-testid="chat-scroll">
        <div style={{ height: scrollHeight }}>
          Messages
          <span ref={bottomRef} data-testid="bottom-anchor" />
        </div>
      </div>
    </div>
  );
}

describe("useChatAutoScroll", () => {
  beforeEach(() => {
    installMatchMedia();
    Object.defineProperty(window, "requestAnimationFrame", {
      writable: true,
      value: vi.fn((callback: FrameRequestCallback) => {
        callback(0);
        return 1;
      }),
    });
    Object.defineProperty(window, "cancelAnimationFrame", {
      writable: true,
      value: vi.fn(),
    });
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it("scrolls to the latest message on initial render", async () => {
    render(<Harness scrollKey="initial" scrollTop={800} />);

    const scroll = screen.getByTestId("chat-scroll") as HTMLDivElement;
    await waitFor(() => expect(scroll.scrollTop).toBe(1000));
    expect(screen.getByTestId("state").textContent).toBe("near");
  });

  it("shows the jump control when the user scrolls away from the bottom", async () => {
    render(<Harness scrollKey="initial" scrollTop={800} />);

    const scroll = screen.getByTestId("chat-scroll") as HTMLDivElement;
    scroll.scrollTop = 240;
    fireEvent.scroll(scroll);

    await waitFor(() => expect((screen.getByLabelText("Jump to latest message") as HTMLButtonElement).hidden).toBe(false));
    expect(screen.getByTestId("state").textContent).toBe("away");
  });

  it("does not yank the user down when new content arrives while reading older messages", async () => {
    const { rerender } = render(<Harness scrollKey="first" scrollTop={800} />);

    const scroll = screen.getByTestId("chat-scroll") as HTMLDivElement;
    scroll.scrollTop = 100;
    fireEvent.scroll(scroll);
    const scrollTo = scroll.scrollTo as ReturnType<typeof vi.fn>;
    scrollTo.mockClear();

    rerender(<Harness scrollKey="second" scrollTop={100} />);

    expect(scrollTo).not.toHaveBeenCalled();
    expect(scroll.scrollTop).toBe(100);
    await waitFor(() => expect((screen.getByLabelText("Jump to latest message") as HTMLButtonElement).hidden).toBe(false));
  });

  it("scrolls to the latest message when the jump control is clicked", async () => {
    render(<Harness scrollKey="initial" scrollTop={800} />);

    const scroll = screen.getByTestId("chat-scroll") as HTMLDivElement;
    scroll.scrollTop = 120;
    fireEvent.scroll(scroll);

    const button = await screen.findByLabelText("Jump to latest message");
    fireEvent.click(button);

    expect(scroll.scrollTop).toBe(1000);
    expect((button as HTMLButtonElement).hidden).toBe(true);
  });

  it("stays quiet when disabled", () => {
    render(<Harness enabled={false} scrollKey="initial" scrollTop={100} />);

    const scroll = screen.getByTestId("chat-scroll") as HTMLDivElement;
    expect(scroll.scrollTop).toBe(100);
    expect((screen.getByLabelText("Jump to latest message") as HTMLButtonElement).hidden).toBe(true);
  });
});
