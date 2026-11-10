import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Meter } from './Meter';

describe('Meter', () => {
  it('reads a value and its limits, its value within the range it gives', () => {
    render(<Meter label="Title length" value={72} min={30} max={60} valueText="72 characters" />);
    const meter = screen.getByRole('meter', { name: 'Title length' });
    expect(meter).toHaveAttribute('aria-valuetext', '72 characters, allowed 30 to 60, over the limit');
    expect(Number(meter.getAttribute('aria-valuenow'))).toBe(72);
  });

  it('keeps aria-valuenow within its scale when the value is past its end, the words giving the value', () => {
    render(<Meter label="Title length" value={150} max={60} scaleMax={100} valueText="150 characters" />);
    const meter = screen.getByRole('meter', { name: 'Title length' });
    expect(Number(meter.getAttribute('aria-valuenow'))).toBeLessThanOrEqual(
      Number(meter.getAttribute('aria-valuemax')),
    );
    expect(meter).toHaveAttribute('aria-valuetext', '150 characters, limit 60, over the limit');
  });

  it('names limits that would print over each other with one tick', () => {
    const { container, rerender } = render(
      <Meter label="Title length" value={569} min={30} max={60} valueText="569 characters" />,
    );
    const ticks = () => [...container.querySelectorAll('.si-meter__tick')].map((tick) => tick.textContent);
    expect(ticks()).toEqual(['30–60']);
    rerender(<Meter label="Title length" value={72} min={30} max={60} valueText="72 characters" />);
    expect(ticks()).toEqual(['30', '60']);
  });
});
