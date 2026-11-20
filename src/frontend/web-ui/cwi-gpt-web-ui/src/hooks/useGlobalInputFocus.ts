import { useCallback, useEffect, useRef, type KeyboardEvent as ReactKeyboardEvent, type RefObject } from "react";

type MainInputElement = HTMLTextAreaElement | HTMLInputElement;

export interface UseGlobalInputFocusOptions {
  inputRef: RefObject<MainInputElement | null>;
  value: string;
  onValueChange: (value: string) => void;
  onSubmit?: () => void;
  enabled?: boolean;
  isModalOpen?: boolean;
  clearOnEscape?: boolean;
}

export interface UseGlobalInputFocusResult {
  onKeyDown: (event: ReactKeyboardEvent<MainInputElement>) => void;
}

const EDITABLE_SELECTOR = [
  "input",
  "textarea",
  "select",
  "[contenteditable='true']",
  "[contenteditable='']",
  "[role='textbox']",
  "[role='combobox']",
  "[role='searchbox']",
].join(",");

function isMobileOrCoarsePointer(): boolean {
  if (typeof window === "undefined") {
    return true;
  }

  return window.matchMedia("(pointer: coarse)").matches
    || /Android|iPhone|iPad|iPod|Mobile/i.test(window.navigator.userAgent);
}

function isEditableElement(target: EventTarget | Element | null): boolean {
  if (!(target instanceof Element)) {
    return false;
  }

  return Boolean(target.closest(EDITABLE_SELECTOR));
}

function hasSelectedPageText(): boolean {
  if (typeof window === "undefined") {
    return false;
  }

  const selection = window.getSelection();
  return Boolean(selection && !selection.isCollapsed && selection.toString().trim());
}

function isPageRootElement(element: Element | null): boolean {
  return element === document.body || element === document.documentElement;
}

function isInputAvailable(input: MainInputElement | null): input is MainInputElement {
  return Boolean(input && !input.disabled && !input.readOnly);
}

function focusInput(input: MainInputElement): void {
  try {
    input.focus({ preventScroll: true });
  } catch {
    input.focus();
  }
}

function shouldIgnoreKeyboardEvent(event: KeyboardEvent): boolean {
  return event.defaultPrevented
    || event.isComposing
    || event.key === "Process"
    || event.key === "Dead"
    || event.ctrlKey
    || event.metaKey
    || event.altKey;
}

function isPrintableKey(key: string): boolean {
  return key.length === 1;
}

function insertTextAtInput(
  input: MainInputElement,
  currentValue: string,
  text: string,
  onValueChange: (value: string) => void,
): void {
  const wasFocused = document.activeElement === input;
  const selectionStart = wasFocused && input.selectionStart !== null ? input.selectionStart : currentValue.length;
  const selectionEnd = wasFocused && input.selectionEnd !== null ? input.selectionEnd : selectionStart;
  const nextValue = `${currentValue.slice(0, selectionStart)}${text}${currentValue.slice(selectionEnd)}`;
  const nextSelection = selectionStart + text.length;

  focusInput(input);
  input.value = nextValue;
  input.setSelectionRange(nextSelection, nextSelection);
  onValueChange(nextValue);

  window.requestAnimationFrame(() => {
    if (document.activeElement === input) {
      input.setSelectionRange(nextSelection, nextSelection);
    }
  });
}

export function useGlobalInputFocus({
  inputRef,
  value,
  onValueChange,
  onSubmit,
  enabled = true,
  isModalOpen = false,
  clearOnEscape = true,
}: UseGlobalInputFocusOptions): UseGlobalInputFocusResult {
  const valueRef = useRef(value);
  valueRef.current = value;

  useEffect(() => {
    valueRef.current = value;
  }, [value]);

  const isActive = useCallback(() => {
    return enabled && !isModalOpen && !isMobileOrCoarsePointer();
  }, [enabled, isModalOpen]);

  useEffect(() => {
    if (!isActive()) {
      return;
    }

    const input = inputRef.current;
    const activeElement = document.activeElement;
    if (
      isInputAvailable(input)
      && (!activeElement || isPageRootElement(activeElement) || activeElement === input)
    ) {
      focusInput(input);
    }
  }, [inputRef, isActive]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (!isActive() || shouldIgnoreKeyboardEvent(event) || hasSelectedPageText()) {
        return;
      }

      const input = inputRef.current;
      if (!isInputAvailable(input)) {
        return;
      }

      const activeElement = document.activeElement;
      if (isEditableElement(event.target) || (activeElement && !isPageRootElement(activeElement) && isEditableElement(activeElement))) {
        return;
      }

      if (event.key === "/") {
        event.preventDefault();
        focusInput(input);
        return;
      }

      if (!isPrintableKey(event.key)) {
        return;
      }

      event.preventDefault();
      insertTextAtInput(input, valueRef.current, event.key, onValueChange);
    };

    window.addEventListener("keydown", onKeyDown);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [inputRef, isActive, onValueChange]);

  const onKeyDown = useCallback((event: ReactKeyboardEvent<MainInputElement>) => {
    if (event.nativeEvent.isComposing || event.key === "Process" || event.key === "Dead") {
      return;
    }

    if (event.key === "Escape") {
      if (clearOnEscape && valueRef.current.length > 0) {
        event.preventDefault();
        onValueChange("");
        return;
      }

      event.currentTarget.blur();
      return;
    }

    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      if (valueRef.current.trim()) {
        onSubmit?.();
      }
    }
  }, [clearOnEscape, onSubmit, onValueChange]);

  return { onKeyDown };
}
