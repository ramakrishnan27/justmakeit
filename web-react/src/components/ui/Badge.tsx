import type { ReactNode } from 'react';

export type BadgeTone = 'accent' | 'model' | 'trial' | 'paid' | 'muted';

export function Badge({ tone = 'muted', mono = false, children }: { tone?: BadgeTone; mono?: boolean; children: ReactNode }) {
  return <span className={`badge badge-${tone}${mono ? ' mono' : ''}`}>{children}</span>;
}
