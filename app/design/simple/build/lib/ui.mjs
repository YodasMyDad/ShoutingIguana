// Small pieces the Simple pages share.
import { icon, sevIcon } from './icons.mjs';
import { esc, num } from '../../../directions/clarity/build/lib/format.mjs';
import { SEVERITY } from './words.mjs';

export { esc, num };
export { duration, dateLong, dateShort, time, plural } from '../../../directions/clarity/build/lib/format.mjs';

// The severity mark with Simple's word as its name.
export const mark = (sev, size = 'lg') =>
  sevIcon(sev, size, SEVERITY[sev]?.word ?? (sev === 'skipped' ? 'Couldn\'t check' : 'Nothing wrong'));
export const badge = (sev, cls = '') =>
  `<span class="sev-badge sev-badge--${sev}${cls ? ' ' + cls : ''}">${sevIcon(sev, 'md', '')}${SEVERITY[sev].word}</span>`;

// "2 serious problems"
export const sevCount = (sev, n) => `${num(n)} ${n === 1 ? SEVERITY[sev].one : SEVERITY[sev].many}`;

// The score ring: an arc of the accent on a neutral track, the number inside. Never a severity colour.
// partial: a crawl that didn't finish draws a dashed arc and says "so far". value null: no score.
export function ring(value, { size = 'lg', partial = false, of = size === 'lg' } = {}) {
  const r = 44, c = 2 * Math.PI * r;
  const stroke = size === 'sm' ? 9 : size === 'md' ? 8 : 7;
  const len = value == null ? 0 : c * value / 100;
  let arc = '';
  if (value != null && !partial) {
    arc = `<circle class="ring__arc" cx="50" cy="50" r="${r}" stroke-width="${stroke}" stroke-dasharray="${len.toFixed(1)} ${c.toFixed(1)}"/>`;
  } else if (value != null) {
    // Dashed, and still only as long as the score: a mask carries the length.
    const id = `ring-mask-${size}-${value}`;
    arc = `<mask id="${id}"><circle cx="50" cy="50" r="${r}" fill="none" stroke="#fff" stroke-width="${stroke + 2}" stroke-dasharray="${len.toFixed(1)} ${c.toFixed(1)}"/></mask><circle class="ring__arc ring__arc--partial" cx="50" cy="50" r="${r}" stroke-width="${stroke}" stroke-dasharray="3.2 2.6" mask="url(#${id})"/>`;
  }
  const label = value == null ? 'No score' : `Score ${value} out of 100${partial ? ', so far' : ''}`;
  const under = value == null ? 'no score' : partial ? 'so far' : 'out of 100';
  return `<span class="ring ring--${size}${value == null ? ' ring--none' : ''}" role="img" aria-label="${label}">
    <svg viewBox="0 0 100 100" aria-hidden="true"><circle class="ring__track" cx="50" cy="50" r="${r}" stroke-width="${stroke}"/>${arc}</svg>
    <span class="ring__value">${value == null ? '–' : value}${of ? `<span class="ring__of">${under}</span>` : ''}</span>
  </span>`;
}

export const btn = (label, { kind = 'secondary', ic = '', size = '', kbd = '', cls = '', attrs = '' } = {}) =>
  `<button class="btn btn--${kind}${size ? ' btn--' + size : ''}${cls ? ' ' + cls : ''}" type="button" ${attrs}>${ic ? icon(ic, { size: 'sm' }) : ''}<span class="btn__label">${label}</span>${kbd ? `<kbd class="kbd kbd--inline">${kbd}</kbd>` : ''}</button>`;

export const checkbox = (label, help, on = true) =>
  `<div class="checkbox" role="checkbox" aria-checked="${on}" tabindex="0"><span class="checkbox__box">${icon('check', { size: 'sm' })}</span><span class="checkbox__label">${label}</span>${help ? `<span class="checkbox__help">${help}</span>` : ''}</div>`;

export const toggle = (label, on) =>
  `<button class="switch${on ? ' is-on' : ''}" type="button" role="switch" aria-checked="${on}" aria-label="${esc(label)}"><span class="switch__track"></span></button>`;
