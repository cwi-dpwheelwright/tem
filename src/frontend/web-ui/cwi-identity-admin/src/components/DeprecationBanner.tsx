import { useEffect, useMemo, useState } from "react";

const REDIRECT_DELAY_MS = 2000;

export function DeprecationBanner({ compact = false }: { compact?: boolean }) {
  const adminConsoleUrl = useMemo(
    () => (import.meta.env.VITE_ADMIN_CONSOLE_URL || "").trim(),
    [],
  );
  const [dismissed, setDismissed] = useState(false);
  const [secondsLeft, setSecondsLeft] = useState(Math.ceil(REDIRECT_DELAY_MS / 1000));

  useEffect(() => {
    if (!adminConsoleUrl || dismissed) {
      return;
    }

    setSecondsLeft(Math.ceil(REDIRECT_DELAY_MS / 1000));
    const timer = window.setTimeout(() => {
      window.location.assign(adminConsoleUrl);
    }, REDIRECT_DELAY_MS);

    const interval = window.setInterval(() => {
      setSecondsLeft((prev) => (prev > 1 ? prev - 1 : prev));
    }, 1000);

    return () => {
      window.clearTimeout(timer);
      window.clearInterval(interval);
    };
  }, [adminConsoleUrl, dismissed]);

  if (!adminConsoleUrl || dismissed) {
    return null;
  }

  return (
    <div className={`deprecation${compact ? " deprecation--compact" : ""}`}>
      <div>
        <div className="deprecation__title">Identity Admin is deprecated</div>
        <div className="deprecation__body">
          Please use the CWI Admin Console instead. Redirecting in {secondsLeft}s.
        </div>
      </div>
      <div className="deprecation__actions">
        <a className="btn btn--primary" href={adminConsoleUrl}>
          Open Admin Console
        </a>
        <button className="btn btn--ghost" type="button" onClick={() => setDismissed(true)}>
          Stay here
        </button>
      </div>
    </div>
  );
}
