import { useCallback, useEffect, useRef, useState, type RefObject } from "react";

export interface UseChatAutoScrollOptions {
  enabled?: boolean;
  bottomThresholdPx?: number;
  defaultBehavior?: ScrollBehavior;
}

export interface UseChatAutoScrollResult {
  isNearBottom: boolean;
  showJumpToBottom: boolean;
  scrollToLatest: (behavior?: ScrollBehavior) => void;
}

function getDistanceFromBottom(element: HTMLElement): number {
  return Math.max(0, element.scrollHeight - element.scrollTop - element.clientHeight);
}

function isNearScrollEnd(element: HTMLElement, thresholdPx: number): boolean {
  return getDistanceFromBottom(element) <= thresholdPx;
}

function prefersReducedMotion(): boolean {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") {
    return false;
  }

  return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

function resolveBehavior(behavior: ScrollBehavior | undefined, fallback: ScrollBehavior): ScrollBehavior {
  if (behavior) {
    return behavior;
  }
  return prefersReducedMotion() ? "auto" : fallback;
}

function scrollElementToEnd(element: HTMLElement, behavior: ScrollBehavior): void {
  if (typeof element.scrollTo === "function") {
    element.scrollTo({ top: element.scrollHeight, behavior });
    return;
  }

  element.scrollTop = element.scrollHeight;
}

export function useChatAutoScroll<TScrollElement extends HTMLElement>(
  scrollRef: RefObject<TScrollElement | null>,
  bottomRef: RefObject<HTMLElement | null>,
  scrollKey: unknown,
  options: UseChatAutoScrollOptions = {},
): UseChatAutoScrollResult {
  const {
    enabled = true,
    bottomThresholdPx = 120,
    defaultBehavior = "smooth",
  } = options;
  const [isNearBottom, setIsNearBottom] = useState(true);
  const [showJumpToBottom, setShowJumpToBottom] = useState(false);
  const userMovedAwayRef = useRef(false);
  const nearBottomRef = useRef(true);
  const hasSeenScrollKeyRef = useRef(false);
  const rafRef = useRef<number | null>(null);

  const updateBottomState = useCallback((nextIsNearBottom: boolean) => {
    nearBottomRef.current = nextIsNearBottom;
    setIsNearBottom(nextIsNearBottom);
    setShowJumpToBottom(!nextIsNearBottom);
  }, []);

  const readBottomState = useCallback(() => {
    const element = scrollRef.current;
    if (!element || !enabled) {
      updateBottomState(true);
      return true;
    }

    const nextIsNearBottom = isNearScrollEnd(element, bottomThresholdPx);
    updateBottomState(nextIsNearBottom);
    return nextIsNearBottom;
  }, [bottomThresholdPx, enabled, scrollRef, updateBottomState]);

  const scrollToLatest = useCallback((behavior?: ScrollBehavior) => {
    const element = scrollRef.current;
    if (!element || !enabled) {
      return;
    }

    const sentinel = bottomRef.current;
    const nextBehavior = resolveBehavior(behavior, defaultBehavior);
    scrollElementToEnd(element, nextBehavior);
    if (sentinel && document.activeElement !== sentinel) {
      sentinel.setAttribute("data-chat-scroll-anchor", "true");
    }
    updateBottomState(true);
  }, [bottomRef, defaultBehavior, enabled, scrollRef, updateBottomState]);

  useEffect(() => {
    if (!enabled) {
      userMovedAwayRef.current = false;
      updateBottomState(true);
      return;
    }

    const element = scrollRef.current;
    if (!element) {
      return;
    }

    const onScroll = () => {
      const nextIsNearBottom = isNearScrollEnd(element, bottomThresholdPx);
      userMovedAwayRef.current = !nextIsNearBottom;
      updateBottomState(nextIsNearBottom);
    };

    element.addEventListener("scroll", onScroll, { passive: true });
    readBottomState();

    return () => {
      element.removeEventListener("scroll", onScroll);
    };
  }, [bottomThresholdPx, enabled, readBottomState, scrollRef, updateBottomState]);

  useEffect(() => {
    if (!enabled) {
      return;
    }

    const isFirstKey = !hasSeenScrollKeyRef.current;
    hasSeenScrollKeyRef.current = true;
    const shouldFollowLatest = isFirstKey || nearBottomRef.current || !userMovedAwayRef.current;
    if (!shouldFollowLatest) {
      setShowJumpToBottom(true);
      return;
    }

    if (rafRef.current !== null) {
      window.cancelAnimationFrame(rafRef.current);
    }

    rafRef.current = window.requestAnimationFrame(() => {
      rafRef.current = null;
      scrollToLatest(isFirstKey ? "auto" : undefined);
    });

    return () => {
      if (rafRef.current !== null) {
        window.cancelAnimationFrame(rafRef.current);
        rafRef.current = null;
      }
    };
  }, [enabled, scrollKey, scrollToLatest]);

  return {
    isNearBottom,
    showJumpToBottom,
    scrollToLatest,
  };
}
