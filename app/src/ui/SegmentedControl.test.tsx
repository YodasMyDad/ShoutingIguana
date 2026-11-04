import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { SegmentedControl } from './SegmentedControl';
import { SeverityMark } from './SeverityMark';

type Filter = 'all' | 'error' | 'warning' | 'notice';

function SeverityFilter({ onChange }: { onChange?: (value: Filter) => void }) {
  const [value, setValue] = useState<Filter>('all');
  return (
    <SegmentedControl<Filter>
      label="Severity"
      value={value}
      onValueChange={(next) => {
        setValue(next);
        onChange?.(next);
      }}
      options={[
        { value: 'all', label: 'All', count: 19 },
        {
          value: 'error',
          label: 'Errors',
          icon: <SeverityMark severity="error" decorative />,
          count: 2,
          iconOnly: true,
        },
        {
          value: 'warning',
          label: 'Warnings',
          icon: <SeverityMark severity="warning" decorative />,
          count: 6,
          iconOnly: true,
        },
        { value: 'notice', label: 'Notices', count: 11, disabled: true },
      ]}
    />
  );
}

describe('SegmentedControl', () => {
  it('is a radio group named by its label, its icon-only segments by theirs', () => {
    render(<SeverityFilter />);
    expect(screen.getByRole('radiogroup', { name: 'Severity' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'All 19' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('radio', { name: 'Errors 2' })).toHaveAttribute('aria-checked', 'false');
  });

  it('chooses as the arrows move, skips what’s disabled, and wraps round', async () => {
    const onChange = vi.fn();
    render(<SeverityFilter onChange={onChange} />);
    await userEvent.tab();
    expect(screen.getByRole('radio', { name: 'All 19' })).toHaveFocus();
    await userEvent.keyboard('{ArrowRight}');
    expect(screen.getByRole('radio', { name: 'Errors 2' })).toHaveFocus();
    expect(screen.getByRole('radio', { name: 'Errors 2' })).toHaveAttribute('aria-checked', 'true');
    await userEvent.keyboard('{ArrowRight}');
    expect(screen.getByRole('radio', { name: 'Warnings 6' })).toHaveAttribute('aria-checked', 'true');
    await userEvent.keyboard('{ArrowRight}');
    expect(screen.getByRole('radio', { name: 'All 19' })).toHaveAttribute('aria-checked', 'true');
    expect(onChange).toHaveBeenNthCalledWith(1, 'error');
    expect(onChange).toHaveBeenNthCalledWith(2, 'warning');
    expect(onChange).toHaveBeenNthCalledWith(3, 'all');
  });

  it('keeps one segment chosen when the chosen one is pressed again', async () => {
    const onChange = vi.fn();
    render(<SeverityFilter onChange={onChange} />);
    await userEvent.click(screen.getByRole('radio', { name: 'Errors 2' }));
    await userEvent.click(screen.getByRole('radio', { name: 'Errors 2' }));
    expect(screen.getByRole('radio', { name: 'Errors 2' })).toHaveAttribute('aria-checked', 'true');
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it('names an icon-only segment by its label and count, and its tooltip doesn’t read the label again', async () => {
    render(<SeverityFilter />);
    const errors = screen.getByRole('radio', { name: 'Errors 2' });
    act(() => errors.focus());
    await screen.findByRole('tooltip');
    expect(errors).toHaveAccessibleDescription('');
  });

  it('reads what each choice means as its segment’s description, chosen or not', () => {
    render(
      <SegmentedControl
        label="Density"
        value="standard"
        onValueChange={() => undefined}
        options={[
          { value: 'compact', label: 'Compact', description: 'Rows of 28 px' },
          { value: 'standard', label: 'Standard', description: 'Rows of 32 px' },
          { value: 'comfortable', label: 'Comfortable' },
        ]}
      />,
    );
    expect(screen.getByRole('radio', { name: 'Compact' })).toHaveAccessibleDescription('Rows of 28 px');
    expect(screen.getByRole('radio', { name: 'Standard' })).toHaveAccessibleDescription('Rows of 32 px');
    expect(screen.getByRole('radio', { name: 'Comfortable' })).not.toHaveAttribute('aria-describedby');
  });
});
