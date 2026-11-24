import { Plus } from 'lucide-react';
import { Button } from '../../ui/Button';
import { TextField } from '../../ui/TextField';
import { cookieRow, type CrawlForm } from './form';
import { FIELD_WORDS, SIMPLE_WORDS } from './words';

/** HTTP sign-in, cookies to send, and a sign-in page. The same values in both modes; Advanced also asks which host and which fields. */
export function AuthFields({
  form,
  onChange,
  onTouch,
  error,
  mode,
}: {
  form: CrawlForm;
  onChange: (form: CrawlForm) => void;
  onTouch?: (() => void) | undefined;
  error: string | null;
  mode: 'simple' | 'advanced';
}) {
  const login = form.httpLogins[0] ?? { host: '', username: '', password: '' };
  const setLogin = (patch: Partial<typeof login>) => {
    const next = { ...login, ...patch };
    const rest = form.httpLogins.slice(1);
    onChange({
      ...form,
      httpLogins: next.username === '' && next.password === '' && next.host === '' ? rest : [next, ...rest],
    });
    onTouch?.();
  };

  return (
    <div className="flex flex-col gap-4">
      {mode === 'simple' ? <p className="text-13">{SIMPLE_WORDS.auth.help}</p> : null}
      <TextField
        // In Advanced the section holds two sign-ins, so each field says whose it is: the first was named only
        // "HTTP sign-in", with nothing to say it took the user name.
        label={mode === 'simple' ? 'User name' : `${FIELD_WORDS.httpLogins.label} user name`}
        description={mode === 'advanced' ? FIELD_WORDS.httpLogins.help : undefined}
        value={login.username}
        autoComplete="off"
        spellCheck={false}
        error={error}
        data-setting="httpLogins"
        onChange={(event) => setLogin({ username: event.target.value })}
        onBlur={onTouch}
      />
      <TextField
        label={mode === 'simple' ? 'Password' : `${FIELD_WORDS.httpLogins.label} password`}
        type="password"
        value={login.password}
        autoComplete="off"
        spellCheck={false}
        onChange={(event) => setLogin({ password: event.target.value })}
        onBlur={onTouch}
      />
      {mode === 'advanced' ? (
        <TextField
          label="Host"
          description="Empty means this site, including its www address."
          value={login.host}
          spellCheck={false}
          placeholder="example.com"
          onChange={(event) => setLogin({ host: event.target.value })}
          onBlur={onTouch}
        />
      ) : null}
      <TextField
        label={mode === 'simple' ? 'Sign-in page' : FIELD_WORDS.formUrl.label}
        description={
          mode === 'simple' ? 'Leave empty if the site asks for the password itself.' : FIELD_WORDS.formUrl.help
        }
        value={form.formUrl}
        type="url"
        inputMode="url"
        spellCheck={false}
        placeholder="https://example.com/login"
        data-setting="formUrl"
        onChange={(event) => onChange({ ...form, formUrl: event.target.value })}
        onBlur={onTouch}
      />
      {mode === 'advanced' ? (
        <>
          <TextField
            label={FIELD_WORDS.formUsername.label}
            description={FIELD_WORDS.formUsername.help}
            value={form.formUsername}
            autoComplete="off"
            spellCheck={false}
            onChange={(event) => onChange({ ...form, formUsername: event.target.value })}
            onBlur={onTouch}
          />
          <TextField
            label={FIELD_WORDS.formPassword.label}
            description={FIELD_WORDS.formPassword.help}
            type="password"
            value={form.formPassword}
            autoComplete="off"
            onChange={(event) => onChange({ ...form, formPassword: event.target.value })}
            onBlur={onTouch}
          />
          <TextField
            label={FIELD_WORDS.formUsernameField.label}
            description={FIELD_WORDS.formUsernameField.help}
            value={form.formUsernameField}
            mono
            spellCheck={false}
            onChange={(event) => onChange({ ...form, formUsernameField: event.target.value })}
            onBlur={onTouch}
          />
          <TextField
            label={FIELD_WORDS.formPasswordField.label}
            description={FIELD_WORDS.formPasswordField.help}
            value={form.formPasswordField}
            mono
            spellCheck={false}
            onChange={(event) => onChange({ ...form, formPasswordField: event.target.value })}
            onBlur={onTouch}
          />
          <TextField
            label={FIELD_WORDS.formSubmit.label}
            description={FIELD_WORDS.formSubmit.help}
            value={form.formSubmit}
            mono
            spellCheck={false}
            onChange={(event) => onChange({ ...form, formSubmit: event.target.value })}
            onBlur={onTouch}
          />
          <div className="flex flex-col gap-3">
            <div>
              <p className="si-field-label">{FIELD_WORDS.cookies.label}</p>
              <p className="si-field-description">{FIELD_WORDS.cookies.help}</p>
            </div>
            {form.cookies.map((row) => (
              <div key={row.key} className="flex flex-wrap items-end gap-2">
                <TextField
                  label="Name"
                  hideLabel
                  value={row.name}
                  spellCheck={false}
                  placeholder="Name"
                  onChange={(event) =>
                    onChange({
                      ...form,
                      cookies: form.cookies.map((cookie) =>
                        cookie.key === row.key ? { ...cookie, name: event.target.value } : cookie,
                      ),
                    })
                  }
                  onBlur={onTouch}
                />
                <TextField
                  label="Value"
                  hideLabel
                  type="password"
                  value={row.value}
                  spellCheck={false}
                  placeholder="Value"
                  onChange={(event) =>
                    onChange({
                      ...form,
                      cookies: form.cookies.map((cookie) =>
                        cookie.key === row.key ? { ...cookie, value: event.target.value } : cookie,
                      ),
                    })
                  }
                  onBlur={onTouch}
                />
                <TextField
                  label="Domain"
                  hideLabel
                  value={row.domain}
                  spellCheck={false}
                  placeholder="Domain"
                  onChange={(event) =>
                    onChange({
                      ...form,
                      cookies: form.cookies.map((cookie) =>
                        cookie.key === row.key ? { ...cookie, domain: event.target.value } : cookie,
                      ),
                    })
                  }
                  onBlur={onTouch}
                />
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() =>
                    onChange({ ...form, cookies: form.cookies.filter((cookie) => cookie.key !== row.key) })
                  }
                >
                  Remove
                </Button>
              </div>
            ))}
            <div>
              <Button
                type="button"
                size="sm"
                icon={Plus}
                onClick={() => onChange({ ...form, cookies: [...form.cookies, cookieRow()] })}
              >
                Add a cookie
              </Button>
            </div>
          </div>
        </>
      ) : null}
    </div>
  );
}
