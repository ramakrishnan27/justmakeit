import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

// The tests run from the web-react folder (npm test), so the repository root is its parent.
const repositoryRoot = resolve(process.cwd(), '..');

/** A file of the repository outside this app (the server's source, a migration), for the tests that keep the two in step. */
export function readRepositoryFile(path: string): string {
  return readFileSync(resolve(repositoryRoot, path), 'utf8');
}
