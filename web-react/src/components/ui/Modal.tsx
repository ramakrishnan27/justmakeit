import { useEffect, useRef, useState, type KeyboardEvent, type ReactNode, type RefObject } from 'react';
import { createPortal } from 'react-dom';
import { UiIcon } from '@/components/icons/UiIcon';

const focusable = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * A modal dialog: it is drawn above the page, the page behind it cannot be reached (it is inert, and does not scroll),
 * Tab stays inside it, and focus goes back to where it came from when it closes.
 *
 * `dismissible` says whether Escape and a tap on the dark area close it. The × button always does, unless
 * `closeDisabled` (a request is in flight and closing now would lose its answer). Content that must not be lost by
 * accident (a secret shown once) turns `dismissible` off, so only a button the person chose can close it.
 *
 * The content names the dialog with a heading whose id is `titleId`; the first control that should have focus takes it
 * itself, and if none does the dialog takes it.
 */
export function Modal({
  titleId,
  onClose,
  dismissible = true,
  closeDisabled = false,
  returnFocus,
  children,
}: {
  titleId: string;
  onClose: () => void;
  dismissible?: boolean;
  closeDisabled?: boolean;
  /** Where focus goes when the dialog closes: the control that opened it. Without it, whatever had focus when it opened. */
  returnFocus?: RefObject<HTMLElement | null>;
  children: ReactNode;
}) {
  const backdrop = useRef<HTMLDivElement>(null);
  const dialog = useRef<HTMLDivElement>(null);
  // Read while drawing, before any effect moves focus into the dialog. (Safari does not focus a button when it is tapped.)
  const [opener] = useState(() => document.activeElement);

  // While the dialog is open everything else on the page is inert and the page does not scroll.
  useEffect(() => {
    const host = backdrop.current;
    if (!host) {
      return;
    }
    const covered: Element[] = [];
    for (const child of Array.from(document.body.children)) {
      if (child !== host && !child.hasAttribute('inert')) {
        child.setAttribute('inert', '');
        covered.push(child);
      }
    }
    document.documentElement.classList.add('modal-open');
    if (!dialog.current?.contains(document.activeElement)) {
      dialog.current?.focus();
    }

    return () => {
      for (const child of covered) {
        child.removeAttribute('inert');
      }
      document.documentElement.classList.remove('modal-open');
      const target = returnFocus?.current ?? opener;
      if (target instanceof HTMLElement) {
        target.focus();
      }
    };
    // Once, when the dialog opens: what it covers and where focus returns are decided then.
  }, []);

  useEffect(() => {
    if (!dismissible) {
      return;
    }
    function onKeydown(event: globalThis.KeyboardEvent) {
      if (event.key === 'Escape') {
        onClose();
      }
    }
    document.addEventListener('keydown', onKeydown);
    return () => document.removeEventListener('keydown', onKeydown);
  }, [dismissible, onClose]);

  // Tab goes round inside the dialog instead of leaving it.
  function keepFocusInside(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key !== 'Tab') {
      return;
    }
    const items = Array.from(dialog.current?.querySelectorAll<HTMLElement>(focusable) ?? []).filter((item) => !item.hidden);
    const first = items[0];
    const last = items[items.length - 1];
    if (!first || !last) {
      event.preventDefault();
      dialog.current?.focus();
      return;
    }
    const active = document.activeElement;
    if (event.shiftKey && (active === first || active === dialog.current)) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  return createPortal(
    // A press that starts on the dark area (not one that starts inside the dialog and ends outside, such as selecting text) closes it.
    <div
      className="modal-backdrop"
      ref={backdrop}
      onMouseDown={(event) => {
        if (dismissible && event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <div className="modal" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1} ref={dialog} onKeyDown={keepFocusInside}>
        <button className="modal-close" type="button" aria-label="Close" disabled={closeDisabled} onClick={onClose}>
          <UiIcon name="close" />
        </button>
        {children}
      </div>
    </div>,
    document.body,
  );
}
