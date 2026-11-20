import { Checkbox } from '../../ui/Checkbox';
import { rememberWords } from './words';

export interface RememberSecretsProps {
  /** The user's consent: the proxy's password and the headers' values go to Windows Credential Manager. */
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
  /** Whose words: Simple's are plain. Settings shows the full form in both modes, with each mode's words here. */
  mode: 'simple' | 'advanced';
  /** What is remembered: the crawl's secrets, or a Shopify signature alone (said of it). */
  subject?: 'secrets' | 'signature' | undefined;
  /** Nothing to remember yet (no signature typed): shown, so the form's words about it hold, and not to be ticked. */
  disabled?: boolean | undefined;
}

/**
 * The option to remember the secrets a crawl is given (docs/v2-ui-architecture.md, "Secrets"): off until the user
 * ticks it, and saying what happens either way: kept in Windows Credential Manager, or for this session only.
 */
export function RememberSecrets({ checked, onCheckedChange, mode, subject, disabled = false }: RememberSecretsProps) {
  const words = rememberWords(mode, checked, subject);
  return (
    <div data-setting="rememberSecrets">
      <Checkbox
        label={words.label}
        description={
          disabled ? 'Paste a signature above, and it can be remembered for the next crawl of this store.' : words.help
        }
        checked={disabled ? false : checked}
        disabled={disabled}
        onCheckedChange={onCheckedChange}
      />
    </div>
  );
}
