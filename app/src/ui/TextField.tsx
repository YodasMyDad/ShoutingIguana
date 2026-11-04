import clsx from 'clsx';
import { X } from 'lucide-react';
import {
  useId,
  useRef,
  useState,
  type ChangeEvent,
  type InputHTMLAttributes,
  type KeyboardEvent,
  type ReactNode,
  type Ref,
} from 'react';
import { Icon, IconSlot, type IconLike } from './Icon';
import { Kbd } from './Kbd';
import { SeverityMark } from './SeverityMark';

export interface TextFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'size' | 'children'> {
  ref?: Ref<HTMLInputElement> | undefined;
  /** Always given. Shown above the field, or, with `hideLabel`, its accessible name only. */
  label: string;
  hideLabel?: boolean | undefined;
  /** Help under the field, read with it. */
  description?: ReactNode | undefined;
  /** What's wrong with the value, inline under it. Never a toast. */
  error?: string | null | undefined;
  /** 28, 32 (the default) or 52 px: `lg` is Home's URL field. */
  size?: 'sm' | 'md' | 'lg' | undefined;
  /** Monospace text, for URLs and patterns. The placeholder stays in the UI font. */
  mono?: boolean | undefined;
  /** Before the text. */
  icon?: IconLike | undefined;
  /** After the text: a button inside the field. */
  end?: ReactNode | undefined;
  /** A chord that focuses the field, shown as keycaps while it's empty and not focused ("Ctrl F"). */
  shortcut?: string | undefined;
  /**
   * Shows a clear button while there's text, and makes Esc clear it. Out of the tab order: Esc does the same from
   * the field. Called after the field is emptied.
   */
  onClear?: (() => void) | undefined;
  clearLabel?: string | undefined;
  /** For the wrapper: placement in its parent. */
  className?: string | undefined;
}

/**
 * A text field with its label, description and inline error. The box carries the 3:1 edge and the focus ring (an
 * outline); disabled fades it, read-only flattens it, an error edges it in red with the error's mark and words.
 */
export function TextField({
  ref,
  label,
  hideLabel = false,
  description,
  error,
  size = 'md',
  mono = false,
  icon,
  end,
  shortcut,
  onClear,
  clearLabel = 'Clear',
  className,
  id,
  disabled,
  readOnly,
  value,
  defaultValue,
  onChange,
  onKeyDown,
  'aria-describedby': describedBy,
  ...rest
}: TextFieldProps) {
  const ownId = useId();
  const inputId = id ?? `${ownId}-input`;
  const descriptionId = `${ownId}-description`;
  const errorId = `${ownId}-error`;
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [filled, setFilled] = useState(() => String(value ?? defaultValue ?? '') !== '');
  const hasText = value === undefined ? filled : String(value) !== '';
  const clearable = onClear !== undefined && hasText && !disabled && !readOnly;

  const setRef = (element: HTMLInputElement | null) => {
    inputRef.current = element;
    if (typeof ref === 'function') ref(element);
    else if (ref) ref.current = element;
  };

  const clear = () => {
    const input = inputRef.current;
    if (input && value === undefined) input.value = '';
    setFilled(false);
    onClear?.();
    input?.focus();
  };

  const change = (event: ChangeEvent<HTMLInputElement>) => {
    setFilled(event.target.value !== '');
    onChange?.(event);
  };

  const keyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    onKeyDown?.(event);
    if (event.defaultPrevented) return;
    // Esc empties a field that has text; an empty one leaves Esc to what's around it (the inspector, an overlay).
    if (event.key === 'Escape' && clearable) {
      event.preventDefault();
      event.stopPropagation();
      clear();
    }
  };

  const describedByIds = clsx(describedBy, description && descriptionId, error && errorId) || undefined;
  const iconSize = size === 'lg' ? 'lg' : size === 'sm' ? 'sm' : 'md';

  return (
    <div className={clsx('si-field-wrap', className)}>
      {hideLabel ? null : (
        <label htmlFor={inputId} className="si-field-label">
          {label}
        </label>
      )}
      <div
        className={clsx('si-field', size !== 'md' && `si-field--${size}`, mono && 'si-field--mono')}
        data-invalid={error ? '' : undefined}
        data-disabled={disabled ? '' : undefined}
        data-readonly={readOnly ? '' : undefined}
      >
        {icon ? (
          <span className="si-field__icon">
            <IconSlot icon={icon} size={iconSize} />
          </span>
        ) : null}
        <input
          {...rest}
          ref={setRef}
          id={inputId}
          className="si-field__input"
          disabled={disabled}
          readOnly={readOnly}
          {...(value === undefined ? {} : { value })}
          {...(defaultValue === undefined ? {} : { defaultValue })}
          onChange={change}
          onKeyDown={keyDown}
          aria-label={hideLabel ? label : undefined}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedByIds}
        />
        {shortcut || clearable || end ? (
          <span className="si-field__end">
            {shortcut && !hasText ? (
              <span className="si-field__hint">
                <Kbd chord={shortcut} size="sm" />
              </span>
            ) : null}
            {clearable ? (
              <button type="button" tabIndex={-1} className="si-field__clear" aria-label={clearLabel} onClick={clear}>
                <Icon icon={X} size="sm" />
              </button>
            ) : null}
            {end}
          </span>
        ) : null}
      </div>
      {error ? (
        <p id={errorId} className="si-field-error">
          <SeverityMark severity="error" size="sm" decorative />
          {error}
        </p>
      ) : null}
      {description ? (
        <p id={descriptionId} className="si-field-description">
          {description}
        </p>
      ) : null}
    </div>
  );
}
