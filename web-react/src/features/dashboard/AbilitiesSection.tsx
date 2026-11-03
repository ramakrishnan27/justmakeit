import { Badge } from '@/components/ui/Badge';
import { Section } from '@/components/ui/Section';
import { abilities } from '@/lib/roles';

function Ability({ text, allowed }: { text: string; allowed: boolean }) {
  return (
    <li className="ability">
      <span>{text}</span>
      <Badge tone={allowed ? 'paid' : 'muted'}>{allowed ? 'Yes' : 'Not for your role'}</Badge>
    </li>
  );
}

/**
 * What the person may do, read from the permissions in their access token. This mirrors the server's policies and
 * only chooses what to say; the server still decides every request.
 */
export function AbilitiesSection({ permissions, workspaceWord }: { permissions: readonly string[]; workspaceWord: string }) {
  const can = abilities(permissions);

  return (
    <Section title="What you can do">
      <ul className="ability-list">
        <Ability text="View the workspaces and everything in them" allowed={can.read} />
        <Ability text="Create and change content" allowed={can.write} />
        <Ability text={`Add ${workspaceWord}s and manage the team`} allowed={can.manage} />
      </ul>
    </Section>
  );
}
