import { useState, type ComponentProps } from 'react';

type FieldProps = ComponentProps<'input'> & { id: string; label: string };

/** A labelled text input. Everything else is passed to the <input>. */
export function TextField({ id, label, ...input }: FieldProps) {
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <input id={id} {...input} />
    </div>
  );
}

/** A labelled password input with a Show / Hide button inside the field. */
export function PasswordField({ id, label, hint, ...input }: FieldProps & { hint?: string }) {
  const [shown, setShown] = useState(false);

  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <div className="input-row">
        <input id={id} {...input} type={shown ? 'text' : 'password'} />
        <button className="button button-quiet" type="button" aria-pressed={shown} onClick={() => setShown((value) => !value)}>
          {shown ? 'Hide' : 'Show'}
        </button>
      </div>
      {hint ? <p className="hint">{hint}</p> : null}
    </div>
  );
}
