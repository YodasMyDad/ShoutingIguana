// Shared by the specs that change the theme on an open page and then run axe.
import type { Page } from '@playwright/test';

/**
 * A theme changed on an open page tweens its colours, and axe measuring part-way through sees a contrast that
 * neither theme has. Waits for the tweens to end. Only transitions: a spinner's animation never does.
 */
export async function colourTweensEnd(page: Page) {
  await page.evaluate(async () => {
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    const tweens = document.getAnimations().filter((animation) => animation instanceof CSSTransition);
    await Promise.all(tweens.map((tween) => tween.finished.catch(() => undefined)));
  });
}
