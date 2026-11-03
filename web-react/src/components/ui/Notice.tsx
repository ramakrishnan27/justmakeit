import type { ReactNode } from 'react';

/** An error message with an optional action (for example "Try again"). Announced to assistive technology when it appears. */
export function ErrorNotice({ message, children }: { message: string; children?: ReactNode }) {
  return (
    <div className="notice notice-error" role="alert">
      <p>{message}</p>
      {children}
    </div>
  );
}

/** A line of loading text. Polite, so it does not interrupt what a screen reader is saying. */
export function Loading({ text = 'Loading…' }: { text?: string }) {
  return (
    <p className="muted" role="status">
      {text}
    </p>
  );
}

/** The text under a form field that failed validation. Always in the page, hidden until it has something to say. */
export function FormError({ id, message }: { id: string; message: string | null }) {
  return (
    <p className="form-error" id={id} role="alert" hidden={message === null}>
      {message}
    </p>
  );
}
