import { cleanup, configure } from '@testing-library/react';
import { afterEach, beforeEach } from 'vitest';
import { installMatchMedia, setWideScreen } from './viewport';

// How long findBy and waitFor keep looking: the default of one second is too short when many test files run at once.
configure({ asyncUtilTimeout: 5000 });

// React's act() environment, so state updates made by the components under test are flushed.
(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

beforeEach(() => {
  installMatchMedia();
  setWideScreen(true);
  window.scrollTo = () => undefined;
});

afterEach(() => {
  cleanup();
  window.history.replaceState({}, '', '/');
});
