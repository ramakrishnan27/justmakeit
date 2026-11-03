import { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import type { Providers } from '@/api/types';
import { loadProviders, splitErrorCode } from '@/lib/google';
import { useApi } from '@/session/SessionContext';

/**
 * Which sign-in methods the server offers. Null until it answers, so a form never flashes fields that are about to
 * disappear.
 */
export function useProviders(): Providers | null {
  const api = useApi();
  const [providers, setProviders] = useState<Providers | null>(null);

  useEffect(() => {
    let active = true;
    void loadProviders(api).then((loaded) => {
      if (active) {
        setProviders(loaded);
      }
    });
    return () => {
      active = false;
    };
  }, [api]);

  return providers;
}

/**
 * The error code the server left in the address when Google sent the person back (`?error=google_denied`). It is read
 * once, when the page opens, and then taken out of the address so a reload does not show the message again.
 */
export function useReturnedErrorCode(): string | null {
  const location = useLocation();
  const navigate = useNavigate();
  // Reading is pure, so React may run it twice in development; the removal below is an effect.
  const [code] = useState(() => splitErrorCode(location.search).code);

  useEffect(() => {
    const { code: present, rest } = splitErrorCode(location.search);
    if (present !== null) {
      navigate({ pathname: location.pathname, search: rest }, { replace: true });
    }
  }, [location.pathname, location.search, navigate]);

  return code;
}

/** Moves keyboard focus to the form control with this name (the first radio button of a group). */
export function focusField(form: HTMLFormElement | null, name: string) {
  const control = form?.elements.namedItem(name);
  const target = control instanceof RadioNodeList ? control[0] : control;
  if (target instanceof HTMLElement) {
    target.focus();
  }
}
