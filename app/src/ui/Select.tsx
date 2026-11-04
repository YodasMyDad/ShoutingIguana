import { ChevronDown } from 'lucide-react';
import { Button } from './Button';
import type { IconLike } from './Icon';
import { DropdownMenu, MenuLabel, MenuRadioGroup, MenuRadioItem } from './Menu';

export interface SelectOption<T extends string> {
  value: T;
  label: string;
  disabled?: boolean | undefined;
}

export interface SelectProps<T extends string> {
  /** What's being chosen ("Sort"): in the button's accessible name before the choice, and over the options. */
  label: string;
  options: readonly SelectOption<T>[];
  value: T;
  onValueChange: (value: T) => void;
  icon?: IconLike | undefined;
  variant?: 'secondary' | 'ghost' | undefined;
  size?: 'sm' | 'md' | undefined;
  disabled?: boolean | undefined;
  align?: 'start' | 'end' | undefined;
  className?: string | undefined;
}

/**
 * A choice from a short list, as a menu button: the button shows what's chosen ("Newest first"), the menu the
 * options with a dot on the chosen one. For long lists or typing to filter, use a combobox instead.
 */
export function Select<T extends string>({
  label,
  options,
  value,
  onValueChange,
  icon,
  variant = 'ghost',
  size = 'md',
  disabled = false,
  align = 'start',
  className,
}: SelectProps<T>) {
  const current = options.find((option) => option.value === value);
  return (
    <DropdownMenu
      align={align}
      trigger={
        <Button
          variant={variant}
          size={size}
          icon={icon}
          iconEnd={ChevronDown}
          disabled={disabled}
          aria-label={`${label}: ${current?.label ?? ''}`}
          className={className}
        >
          {current?.label ?? label}
        </Button>
      }
    >
      <MenuLabel>{label}</MenuLabel>
      <MenuRadioGroup
        value={value}
        onValueChange={(next) => {
          const option = options.find((candidate) => candidate.value === next);
          if (option) onValueChange(option.value);
        }}
      >
        {options.map((option) => (
          <MenuRadioItem key={option.value} value={option.value} disabled={option.disabled}>
            {option.label}
          </MenuRadioItem>
        ))}
      </MenuRadioGroup>
    </DropdownMenu>
  );
}
