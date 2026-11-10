export type Step = 'create' | 'customize' | 'publish';

const steps: ReadonlyArray<{ value: Step; label: string }> = [
  { value: 'create', label: 'Create Content' },
  { value: 'customize', label: 'Customize' },
  { value: 'publish', label: 'Schedule & Publish' },
];

/**
 * The three steps of the flow. Each is a button that goes straight to its step, so nothing is locked behind the one before; the step
 * a person is on is marked for assistive technology as well as by colour.
 */
export function Stepper({ step, onStep }: { step: Step; onStep: (step: Step) => void }) {
  return (
    <nav className="stepper" aria-label="Steps">
      <ol>
        {steps.map((entry, index) => (
          <li key={entry.value}>
            <button type="button" className="step" aria-current={step === entry.value ? 'step' : undefined} onClick={() => onStep(entry.value)}>
              <span className="step-number" aria-hidden="true">
                {index + 1}
              </span>
              <span>{entry.label}</span>
            </button>
          </li>
        ))}
      </ol>
    </nav>
  );
}
