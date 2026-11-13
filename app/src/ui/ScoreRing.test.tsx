import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { bandOf, partialDashes, scoreLabel, scoreWord } from './score';
import { ScoreRing } from './ScoreRing';

const CIRCUMFERENCE = 2 * Math.PI * 44;
const sum = (dashes: string) => dashes.split(' ').reduce((total, part) => total + Number(part), 0);
/** The length drawn: every dash of the array but the closing gap. */
const drawn = (dashes: string) => {
  const parts = dashes.split(' ').map(Number);
  return parts.filter((_, index) => index % 2 === 0).reduce((total, part) => total + part, 0);
};

describe('ScoreRing', () => {
  it('reads as one image: the score, out of 100, and its word', () => {
    render(<ScoreRing value={85} band="good" />);
    expect(screen.getByRole('img', { name: 'Score 85 out of 100, Good' })).toHaveTextContent('85out of 100');
  });

  it('says "so far" for a partial score, in the ring and in its word', () => {
    render(<ScoreRing value={71} band="good" partial size="sm" word />);
    const ring = screen.getByRole('img', { name: 'Score 71 out of 100 so far, Good so far' });
    expect(ring).toHaveTextContent('Good so far');
  });

  it('shows no score as a dashed track and "–", never 0', () => {
    const { container } = render(<ScoreRing value={null} />);
    expect(screen.getByRole('img', { name: 'No score' })).toHaveTextContent('–no score');
    expect(container.querySelectorAll('circle')).toHaveLength(1);
    expect(container.querySelector('circle')).toHaveAttribute('stroke-dasharray', '2 5');
  });

  it('is one image with its word, not two', () => {
    render(<ScoreRing value={85} band="good" size="sm" word />);
    expect(screen.getAllByRole('img')).toHaveLength(1);
  });

  it('draws the arc as long as the score, in the accent', () => {
    const { container } = render(<ScoreRing value={85} band="good" />);
    const arc = container.querySelectorAll('circle')[1];
    expect(arc).toHaveClass('stroke-accent');
    expect(Number(arc?.getAttribute('stroke-dasharray')?.split(' ')[0])).toBeCloseTo(CIRCUMFERENCE * 0.85, 1);
  });

  it('draws no arc at 0', () => {
    const { container } = render(<ScoreRing value={0} band="poor" />);
    expect(container.querySelectorAll('circle')).toHaveLength(1);
  });
});

describe('partialDashes', () => {
  it.each([1, 10, 33.3, 85, 99, 100])('draws dashes over %s% of the circle, then a gap past its end', (percent) => {
    const length = (CIRCUMFERENCE * percent) / 100;
    const dashes = partialDashes(length);
    expect(dashes.split(' ').length % 2).toBe(0);
    expect(sum(dashes)).toBeGreaterThanOrEqual(length + CIRCUMFERENCE - 0.05);
    expect(drawn(dashes)).toBeLessThanOrEqual(length + 0.05);
  });
});

describe('the score’s words', () => {
  it('gives the band of each value as the design draws them', () => {
    expect([100, 90, 89, 70, 69, 50, 49, 0].map(bandOf)).toEqual([
      'veryGood',
      'veryGood',
      'good',
      'good',
      'needsWork',
      'needsWork',
      'poor',
      'poor',
    ]);
    expect(scoreWord('needsWork')).toBe('Needs work');
    expect(scoreWord('veryGood', true)).toBe('Very good so far');
    expect(scoreLabel(40, 'poor')).toBe('Score 40 out of 100, Poor');
  });
});
