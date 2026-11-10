import { useId, useRef, type ReactNode } from 'react';
import { Modal } from '@/components/ui/Modal';

/**
 * A question with two answers, in a dialog: what is about to happen, a button that does it and one that backs out. Focus starts on
 * the harmless button, and the dialog cannot be closed while the request is on its way. An error from the request is shown in it.
 */
export function ConfirmDialog({
  title,
  children,
  confirmLabel,
  cancelLabel = 'Cancel',
  busy,
  error,
  danger = false,
  onConfirm,
  onCancel,
}: {
  title: string;
  children: ReactNode;
  confirmLabel: string;
  cancelLabel?: string;
  busy: boolean;
  error: string | null;
  danger?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const titleId = useId();
  const cancel = useRef<HTMLButtonElement>(null);

  return (
    <Modal titleId={titleId} onClose={onCancel} closeDisabled={busy} dismissible={!busy}>
      <h2 id={titleId}>{title}</h2>
      <div className="confirm-body">{children}</div>
      {error ? (
        <p className="form-error" role="alert">
          {error}
        </p>
      ) : null}
      <div className="confirm-actions">
        <button className="button" type="button" ref={cancel} autoFocus disabled={busy} onClick={onCancel}>
          {cancelLabel}
        </button>
        <button className={`button ${danger ? 'button-danger' : 'button-primary'}`} type="button" disabled={busy} onClick={onConfirm}>
          {busy ? 'Working…' : confirmLabel}
        </button>
      </div>
    </Modal>
  );
}
