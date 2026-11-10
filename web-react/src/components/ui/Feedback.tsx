/** What a screen tells the person after something they did: a change that went through, or one the server refused. */
export interface Feedback {
  tone: 'success' | 'error';
  text: string;
}

/** One line of feedback. A refusal is announced at once (an alert); a success politely (a status). */
export function FeedbackLine({ feedback }: { feedback: Feedback | null }) {
  if (!feedback) {
    return null;
  }

  return (
    <p className={`feedback feedback-${feedback.tone}`} role={feedback.tone === 'error' ? 'alert' : 'status'}>
      {feedback.text}
    </p>
  );
}
