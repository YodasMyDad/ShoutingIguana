import { Search } from 'lucide-react';
import { TextField, type TextFieldProps } from './TextField';

export interface SearchFieldProps extends Omit<TextFieldProps, 'type' | 'icon' | 'onClear' | 'clearable'> {
  /** Called once the field is emptied by its clear button or Esc, after `onChange` has heard the empty value. */
  onClear?: (() => void) | undefined;
}

/**
 * A search box: the magnifier, a clear button and Esc while there's text, and the key that focuses it as keycaps
 * (`shortcut="mod+f"`). Its label is its accessible name, and isn't shown by default: the placeholder says what it
 * searches.
 */
export function SearchField({ hideLabel = true, onClear, clearLabel = 'Clear the search', ...rest }: SearchFieldProps) {
  return (
    <TextField
      {...rest}
      type="search"
      icon={Search}
      hideLabel={hideLabel}
      clearLabel={clearLabel}
      autoComplete="off"
      spellCheck={false}
      clearable
      onClear={onClear}
    />
  );
}
