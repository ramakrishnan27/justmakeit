import { h } from '../core/dom.js';
import { googleRegisterPath, loadProviders, takeErrorCode } from '../core/google.js';
import { messageForGoogleError, messageForRegisterError, validateRegistration } from '../core/messages.js';
import { businessModels, organizationNameLabel } from '../core/roles.js';


/**
 * The sign-up page. First the kind of organization and its name, then how to sign up: with Google, or with an
 * email and password (those fields appear only when that is chosen). The router gives it `context = { api, session }`.
 */
class MemberRegister extends HTMLElement {
  connectedCallback() {
    const { api, session } = this.context;
    // Unknown until the server answers, so the form never flashes fields that are about to disappear.
    let providers = null;

    const error = h('p', { class: 'form-error', id: 'register-error', role: 'alert', hidden: true });
    const showError = (message, field) => {
      error.textContent = message;
      error.hidden = false;
      field?.focus();
    };

    // --- What kind of organization ---------------------------------------------------------
    const modelInputs = businessModels.map((model) =>
      h('input', { type: 'radio', name: 'businessModel', value: model.value, id: `model-${model.value}`, required: true }),
    );
    const models = h(
      'fieldset',
      { class: 'field choices' },
      h('legend', {}, 'What kind of organization is this?'),
      businessModels.map((model, index) =>
        h(
          'label',
          { class: 'choice', for: `model-${model.value}` },
          modelInputs[index],
          h('span', { class: 'choice-title' }, model.title),
          h('span', { class: 'choice-text' }, model.description),
        ),
      ),
    );

    const organizationLabel = h('label', { for: 'organizationName' }, organizationNameLabel(null));
    const organizationName = h('input', {
      id: 'organizationName',
      name: 'organizationName',
      type: 'text',
      autocomplete: 'organization',
      maxlength: '200',
      required: true,
    });

    // --- How to sign up --------------------------------------------------------------------
    const methodInputs = {
      google: h('input', { type: 'radio', name: 'method', value: 'google', id: 'method-google' }),
      email: h('input', { type: 'radio', name: 'method', value: 'email', id: 'method-email' }),
    };
    const methods = h(
      'fieldset',
      { class: 'field choices', hidden: true },
      h('legend', {}, 'How do you want to sign up?'),
      h('label', { class: 'choice choice-compact', for: 'method-google' }, methodInputs.google, h('span', { class: 'choice-title' }, 'Continue with Google')),
      h('label', { class: 'choice choice-compact', for: 'method-email' }, methodInputs.email, h('span', { class: 'choice-title' }, 'Email and password')),
    );

    // --- Email and password: shown only when that is chosen ---------------------------------
    const fullName = h('input', { id: 'fullName', name: 'fullName', type: 'text', autocomplete: 'name', maxlength: '200' });
    const email = h('input', {
      id: 'email',
      name: 'email',
      type: 'email',
      autocomplete: 'username',
      inputmode: 'email',
      autocapitalize: 'none',
      spellcheck: 'false',
    });
    const password = h('input', { id: 'password', name: 'password', type: 'password', autocomplete: 'new-password', minlength: '10', maxlength: '128' });
    const toggle = h('button', { class: 'button button-quiet', type: 'button', 'aria-pressed': 'false' }, 'Show');
    toggle.addEventListener('click', () => {
      const reveal = password.type === 'password';
      password.type = reveal ? 'text' : 'password';
      toggle.textContent = reveal ? 'Hide' : 'Show';
      toggle.setAttribute('aria-pressed', String(reveal));
    });
    const emailFields = h(
      'div',
      { id: 'email-fields', hidden: true },
      h('div', { class: 'field' }, h('label', { for: 'fullName' }, 'Your name (optional)'), fullName),
      h('div', { class: 'field' }, h('label', { for: 'email' }, 'Email'), email),
      h(
        'div',
        { class: 'field' },
        h('label', { for: 'password' }, 'Password'),
        h('div', { class: 'input-row' }, password, toggle),
        h('p', { class: 'hint' }, 'At least 10 characters.'),
      ),
    );

    const submit = h('button', { class: 'button button-primary button-block', type: 'submit' }, 'Create account');

    const chosenModel = () => modelInputs.find((input) => input.checked)?.value ?? '';
    const chosenMethod = () => {
      if (!providers) {
        return '';
      }
      if (!providers.google) {
        return 'email';
      }
      return methodInputs.google.checked ? 'google' : methodInputs.email.checked ? 'email' : '';
    };

    function refresh() {
      organizationLabel.textContent = organizationNameLabel(chosenModel());
      const method = chosenMethod();
      emailFields.hidden = method !== 'email';
      submit.textContent = method === 'google' ? 'Continue with Google' : 'Create account';
      submit.hidden = method === '';
    }

    for (const input of [...modelInputs, methodInputs.google, methodInputs.email]) {
      input.addEventListener('change', refresh);
    }

    const form = h(
      'form',
      {
        novalidate: true,
        onsubmit: async (event) => {
          event.preventDefault();
          error.hidden = true;

          const details = {
            businessModel: chosenModel(),
            organizationName: organizationName.value,
            method: chosenMethod(),
            fullName: fullName.value,
            email: email.value,
            password: password.value,
          };
          const problem = validateRegistration(details);
          if (problem) {
            const fields = { businessModel: modelInputs[0], organizationName, method: methodInputs.email, fullName, email, password };
            showError(problem.message, fields[problem.field]);
            return;
          }

          if (details.method === 'google') {
            // A plain navigation: the server sends the browser to Google and brings it back signed in.
            window.location.assign(googleRegisterPath(details));
            return;
          }

          submit.disabled = true;
          submit.textContent = 'Creating account…';
          form.setAttribute('aria-busy', 'true');
          try {
            // Success changes the session, and the router then moves the page to the dashboard.
            await session.register({
              email: details.email.trim(),
              password: details.password,
              fullName: details.fullName.trim() || null,
              organizationName: details.organizationName.trim(),
              businessModel: details.businessModel,
            });
          } catch (failure) {
            showError(messageForRegisterError(failure));
            password.value = '';
          } finally {
            submit.disabled = false;
            submit.textContent = 'Create account';
            form.removeAttribute('aria-busy');
          }
        },
      },
      models,
      h('div', { class: 'field' }, organizationLabel, organizationName),
      methods,
      emailFields,
      error,
      submit,
    );

    // A message about a missing choice should not outlive the person fixing it.
    form.addEventListener('input', () => {
      error.hidden = true;
    });

    const googleMessage = messageForGoogleError(takeErrorCode());
    if (googleMessage) {
      showError(googleMessage);
    }

    this.replaceChildren(
      h(
        'main',
        { id: 'main', class: 'auth-page' },
        h(
          'section',
          { class: 'card auth-card auth-card-wide', 'aria-labelledby': 'register-title' },
          h('p', { class: 'brand' }, 'Platform'),
          h('h1', { id: 'register-title', tabindex: '-1' }, 'Create your account'),
          h('p', { class: 'muted' }, 'Choose how you work, then how you want to sign up.'),
          form,
          h('p', { class: 'auth-switch' }, 'Already have an account? ', h('a', { href: '/login', 'data-link': true }, 'Sign in')),
        ),
      ),
    );
    refresh();

    // Google is offered only when the server has it set up. Without it there is one way to sign up, so no choice is shown.
    loadProviders(api).then((loaded) => {
      if (!this.isConnected) {
        return;
      }
      providers = loaded;
      methods.hidden = !providers.google;
      if (providers.google) {
        // Both methods are on offer and neither is preselected: the person chooses.
        methodInputs.email.checked = false;
      }
      refresh();
    });
  }
}

customElements.define('member-register', MemberRegister);
