# CWI GPT Web UI Accessibility Checklist

## Scope
- Pages: /trust, /ask, /action, /trace/[traceId], /settings.

## Setup
1. Use keyboard-only navigation (Tab, Shift+Tab, Enter, Space).
2. Use a screen reader (NVDA on Windows or VoiceOver on macOS).
3. Use a contrast checker (browser DevTools or WCAG contrast tool).

## Checklist (Pass/Fail)
- Keyboard navigation reaches all interactive elements in a logical order.
- Visible focus state is always present when tabbing.
- Every form input has a clear label announced by the screen reader.
- Error messages are announced (role="alert") and do not rely on color alone.
- Status messages are announced (role="status") without stealing focus.
- Links and buttons are semantic elements (no clickable divs).
- Text contrast meets WCAG AA (4.5:1 for normal text, 3:1 for large text).

## Page-Specific Checks
### /trust
- Verify pack button reachable by keyboard.
- Error message announced and retry available.
- Verification status is readable and does not rely on color alone.

### /ask
- Question label announced by screen reader.
- Submit button reachable and activates with Enter/Space.
- Error banner announced and the retry path is explicit.

### /action
- Action choices announced within a fieldset/legend.
- Radio buttons change with arrow keys and Space.
- Approval status is readable and does not rely on color alone.

### /trace/[traceId]
- Loading and empty states are visible and readable.
- Error banner announced and retry button reachable.

### /settings
- All inputs have labels and helper text is announced.
- Save/clear buttons are reachable and have clear names.
- Diagnostics copy button is keyboard accessible.

## Evidence
- Record date, tester, browser version, screen reader, and notes.
- Capture any violations and link to remediation PRs.
