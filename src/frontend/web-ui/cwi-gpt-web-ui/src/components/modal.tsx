"use client";

import { useEffect, type ReactNode } from "react";

interface ModalProps {
  ariaLabel: string;
  onClose: () => void;
  children: ReactNode;
  dismissible?: boolean;
}

export function Modal({ ariaLabel, onClose, children, dismissible = true }: ModalProps) {
  useEffect(() => {
    if (!dismissible) {
      return;
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onClose();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [dismissible, onClose]);

  return (
    <div
      className="modal-backdrop"
      role="presentation"
      onClick={(event) => {
        if (dismissible && event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <div
        className="modal-shell"
        role="dialog"
        aria-modal="true"
        aria-label={ariaLabel}
      >
        {dismissible ? (
          <button className="modal-close" type="button" onClick={onClose}>
            Close
          </button>
        ) : null}
        <div className="modal-body">{children}</div>
      </div>
    </div>
  );
}
