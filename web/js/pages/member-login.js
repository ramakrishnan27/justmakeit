import { h } from '../core/dom.js';
import { googleLoginPath, loadProviders, takeErrorCode } from '../core/google.js';
import { messageForGoogleError, messageForSignInError, validateSignIn } from '../core/messages.js';

/** The member sign-in page. The router gives it `context = { api, session }` before it is attached. */
class MemberLogin extends HTMLElement {
  connectedCallback() {
    const { api, session } = this.context;

    const error = h('p', { class: 'form-error', id: 'login-error', role: 'alert', hidden: true });
    const showError = (message) => {
      error.textContent = message;
      error.hidden = false;
    };

    const email = h('input', {
      id: 'email',
      name: 'email',
      type: 'email',
      autocomplete: 'username',
      inputmode: 'email',
      autocapitalize: 'none',
      spellcheck: 'false',
      required: true,
      'aria-describedby': 'login-error',
    });
    const password = h('input', {
      id: 'password',
      name: 'password',
      type: 'password',
      autocomplete: 'current-password',
      required: true,
      'aria-describedby': 'login-error',
    });
    const toggle = h('button', { class: 'button button-quiet', type: 'button', 'aria-pressed': 'false' }, 'Show');
    const submit = h('button', { class: 'button button-primary button-block', type: 'submit' }, 'Sign in');

    toggle.addEventListener('click', () => {
      const reveal = password.type === 'password';
      password.type = reveal ? 'text' : 'password';
      toggle.textContent = reveal ? 'Hide' : 'Show';
      toggle.setAttribute('aria-pressed', String(reveal));
    });

    const form = h(
      'form',
      {
        novalidate: true,
        onsubmit: async (event) => {
          event.preventDefault();
          error.hidden = true;

          const problem = validateSignIn(email.value, password.value);
          if (problem) {
            showError(problem.message);
            (problem.field === 'email' ? email : password).focus();
            return;
          }

          submit.disabled = true;
          submit.textContent = 'Signing in…';
          form.setAttribute('aria-busy', 'true');
          try {
            // Success changes the session, and the router then moves the page to the dashboard.
            await session.signIn(email.value.trim(), password.value);
          } catch (failure) {
            showError(messageForSignInError(failure));
            password.value = '';
            password.focus();
          } finally {
            submit.disabled = false;
            submit.textContent = 'Sign in';
            form.removeAttribute('aria-busy');
          }
        },
      },
      h('div', { class: 'field' }, h('label', { for: 'email' }, 'Email'), email),
      h('div', { class: 'field' }, h('label', { for: 'password' }, 'Password'), h('div', { class: 'input-row' }, password, toggle)),
      error,
      submit,
    );

    // The Google button is added only when the server has Google set up, so it never leads to a dead end.
    const googleRegion = h('div', { class: 'google-region' });

    const message = messageForGoogleError(takeErrorCode());
    if (message) {
      showError(message);
    }

    this.replaceChildren(
      h(
        'main',
        { id: 'main', class: 'auth-page' },
        h(
          'section',
          { class: 'card auth-card', 'aria-labelledby': 'login-title' },
          h('p', { class: 'brand' }, 'Platform'),
          h('h1', { id: 'login-title', tabindex: '-1' }, 'Sign in'),
          h('p', { class: 'muted' }, 'Welcome back. Sign in to your organization.'),
          googleRegion,
          form,
          h('p', { class: 'auth-switch' }, 'New here? ', h('a', { href: '/register', 'data-link': true }, 'Create an account')),
        ),
      ),
    );

    loadProviders(api).then((providers) => {
      if (providers.google && this.isConnected) {
        googleRegion.replaceChildren(
          h('a', { class: 'button button-google button-block', href: googleLoginPath }, 'Continue with Google'),
          h('p', { class: 'divider' }, h('span', {}, 'or sign in with email')),
        );
      }
    });
  }
}

customElements.define('member-login', MemberLogin);
