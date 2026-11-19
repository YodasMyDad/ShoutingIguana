const G = '/crawls/33454603b74b57f1';
export default async ({ page, shot, base, log }) => {
  const go = async (p, name, wait = 2500) => {
    await page.goto(base + p);
    await page.waitForTimeout(wait);
    if (name) await shot(name);
  };
  await go(G + '/overview', 'a02-overview');
  await page.mouse.wheel(0, 1400);
  await page.waitForTimeout(600);
  await shot('a02b-overview-scrolled');
  await go(G + '/issues', 'a03-issues');
  await go(G + '/issues/internal-4xx', 'a04-issues-4xx');
  log('url after 4xx:', page.url());
  await go(G + '/issues/broken-internal-links', 'a05-issues-broken-links');
  await go(G + '/issues/large-images', 'a06-issues-large-images');
  await go(G + '/issues/title-too-long', 'a07-issues-title-long');
  await go(G + '/urls', 'a08-urls');
  await go(G + '/links', 'a09-links');
  await go(G + '/sitemaps', 'a10-sitemaps');
  await go(G + '/robots', 'a11-robots');
  await go(G + '/report', 'a12-report', 4000);
  await go(G + '/settings', 'a13-crawl-settings');
  await go('/settings', 'a14-settings');
  await go('/new', 'a15-new');
  // the ids of the checks, for later walks
  await go(G + '/issues');
  const ids = await page.$$eval('a[href*="/issues/"]', (as) => as.map((a) => a.getAttribute('href').split('/issues/')[1] + ' :: ' + a.textContent.trim().slice(0, 70)));
  log(ids.join('\n'));
};
