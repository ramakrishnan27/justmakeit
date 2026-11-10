import { CONTENT_TYPES, type ContentType } from '@/api/content';
import { ContentTypeIcon } from '../icons';

/** The five kinds of content as buttons, the chosen one marked. Changing the kind keeps everything already written. */
export function ContentTypePicker({ value, onChange, disabled }: { value: ContentType; onChange: (type: ContentType) => void; disabled: boolean }) {
  return (
    <section className="panel" aria-labelledby="panel-type">
      <h2 id="panel-type">Content Type</h2>
      <div className="type-picker" role="group" aria-labelledby="panel-type">
        {CONTENT_TYPES.map((type) => (
          <button
            key={type.value}
            type="button"
            className="type-button"
            aria-pressed={value === type.value}
            disabled={disabled}
            onClick={() => onChange(type.value)}
          >
            <ContentTypeIcon type={type.value} />
            <span>{type.label}</span>
          </button>
        ))}
      </div>
    </section>
  );
}

/** "AI Assistant": switched off until the assistant exists. */
export function AiPanel({ available }: { available: boolean }) {
  return (
    <section className="panel panel-off" aria-labelledby="panel-ai">
      <h2 id="panel-ai">AI Assistant</h2>
      <div className="input-row">
        <input type="text" disabled aria-label="Ask the AI assistant" placeholder="e.g. Write a catchy caption about creativity…" />
        <button className="button" type="button" disabled>
          Generate
        </button>
      </div>
      <p className="muted small">{available ? 'The assistant is not available in this version yet.' : 'The AI assistant is coming soon.'}</p>
    </section>
  );
}
