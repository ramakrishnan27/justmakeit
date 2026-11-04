/** What the team page tells the person after something they did: a change that went through, or one the server refused. */
export interface Feedback {
  tone: 'success' | 'error';
  text: string;
}
