import { Checkbox } from '../../ui/Checkbox';
import { rememberWords } from './words';

export interface RememberSecretsProps {
  /** The user's consent: the proxy's password and the headers' values go to Windows Credential Manager. */
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
  /** Whose words: Simple's are plain. Settings shows the full form in both modes, with each mode's words here. */
  mode: 'simple' | 'advanced';
}

/**
 * The option to remember the secrets a crawl is given (docs/v2-ui-architecture.md, "Secrets"): off until the user
 * ticks it, and saying what happens either way: kept in Windows Credential Manager, or for this session only.
 */
export function RememberSecrets({ checked, onCheckedChange, mode }: RememberSecretsProps) {
  const words = rememberWords(mode, checked);
  return (
    <div data-setting="rememberSecrets">
      <Checkbox label={words.label} description={words.help} checked={checked} onCheckedChange={onCheckedChange} />
    </div>
  );
}
