import { useId, useState, type KeyboardEvent } from 'react';

/**
 * A list of short words shown as chips, with a box to add another: for hashtags and tags. Enter or a comma adds what is typed (and,
 * for `splitOnSpace`, a space does too, because a hashtag has no spaces); pasting several at once adds each. Each chip has a button
 * that removes it. `suggestions` are offered by the browser's own list under the box.
 */
export function ChipInput({
  label,
  values,
  onAdd,
  onRemove,
  prefix = '',
  placeholder,
  suggestions = [],
  onType,
  disabled = false,
  splitOnSpace = false,
  hint,
  problem,
}: {
  label: string;
  values: readonly string[];
  /** Called with one word to add (already split). */
  onAdd: (word: string) => void;
  onRemove: (value: string) => void;
  /** Shown before each chip: "#" for a hashtag. */
  prefix?: string;
  placeholder?: string;
  suggestions?: readonly string[];
  /** Called with what has been typed so far, for a page that fetches suggestions. */
  onType?: (text: string) => void;
  disabled?: boolean;
  splitOnSpace?: boolean;
  hint?: string;
  problem?: string | null;
}) {
  const id = useId();
  const [text, setText] = useState('');
  const separators = splitOnSpace ? /[\s,]+/ : /,/;

  function commit(raw: string) {
    for (const word of raw.split(separators)) {
      if (word.trim().length > 0) {
        onAdd(word.trim());
      }
    }
    setText('');
    onType?.('');
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Enter' || event.key === ',' || (splitOnSpace && event.key === ' ')) {
      if (text.trim().length > 0) {
        event.preventDefault();
        commit(text);
      } else if (event.key === 'Enter') {
        // An empty box must not submit the form around it.
        event.preventDefault();
      }
    } else if (event.key === 'Backspace' && text.length === 0 && values.length > 0 && !disabled) {
      onRemove(values[values.length - 1] ?? '');
    }
  }

  return (
    <div className="field chip-field">
      <label htmlFor={`${id}-input`}>{label}</label>
      {values.length > 0 ? (
        <ul className="chips" aria-label={label}>
          {values.map((value) => (
            <li key={value} className="chip">
              <span>
                {prefix}
                {value}
              </span>
              <button type="button" className="chip-remove" aria-label={`Remove ${prefix}${value}`} disabled={disabled} onClick={() => onRemove(value)}>
                ×
              </button>
            </li>
          ))}
        </ul>
      ) : null}
      <input
        id={`${id}-input`}
        type="text"
        autoComplete="off"
        placeholder={placeholder}
        value={text}
        disabled={disabled}
        list={suggestions.length > 0 ? `${id}-list` : undefined}
        aria-describedby={problem ? `${id}-problem` : hint ? `${id}-hint` : undefined}
        onChange={(event) => {
          const next = event.target.value;
          // Pasting "#a #b" or "a, b" adds each; typing one word waits for Enter.
          if (/[,\n]/.test(next) || (splitOnSpace && /\s\S/.test(next))) {
            commit(next);
          } else {
            setText(next);
            onType?.(next);
          }
        }}
        onKeyDown={onKeyDown}
        onBlur={() => text.trim().length > 0 && commit(text)}
      />
      {suggestions.length > 0 ? (
        <datalist id={`${id}-list`}>
          {suggestions.map((suggestion) => (
            <option key={suggestion} value={suggestion} />
          ))}
        </datalist>
      ) : null}
      {problem ? (
        <p className="form-error" id={`${id}-problem`} role="alert">
          {problem}
        </p>
      ) : hint ? (
        <p className="hint" id={`${id}-hint`}>
          {hint}
        </p>
      ) : null}
    </div>
  );
}
