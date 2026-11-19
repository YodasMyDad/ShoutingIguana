// What a row gives whoever makes the fix (the UX audit of 2026-11-19, S-13 and S-16).
import { describe, expect, it } from 'vitest';
import { cutTextOf, settingNames, sitemapLine } from './developerNotes';

describe('a security check’s settings, by name', () => {
  it('names the headers the detail lists, for the two checks that list them', () => {
    expect(
      settingNames({ id: 'security-headers-missing' }, 'Missing Content-Security-Policy, Referrer-Policy'),
    ).toEqual(['Content-Security-Policy', 'Referrer-Policy']);
    expect(
      settingNames({ id: 'security-headers-weak' }, 'Strict-Transport-Security max-age is 10,368,000 s, under a year'),
    ).toEqual(['Strict-Transport-Security']);
    expect(settingNames({ id: 'security-headers-missing' }, 'Missing X-Content-Type-Options')).toEqual([
      'X-Content-Type-Options',
    ]);
  });

  it('names nothing for any other check, or with no detail', () => {
    expect(settingNames({ id: 'title-missing' }, 'Missing Content-Security-Policy')).toEqual([]);
    expect(settingNames({ id: 'security-headers-missing' }, null)).toEqual([]);
  });
});

describe('the line robots.txt needs', () => {
  it('gives the Sitemap line for the sitemap the check found', () => {
    expect(
      sitemapLine(
        { id: 'robots-txt-no-sitemap' },
        'https://www.example.com/robots.txt is empty, so it has no Sitemap: line; the sitemap was found at https://www.example.com/sitemap.xml',
      ),
    ).toBe('Sitemap: https://www.example.com/sitemap.xml');
    expect(
      sitemapLine(
        { id: 'robots-txt-no-sitemap' },
        'It has no Sitemap: line; the sitemap was found at https://a.example/s.xml.',
      ),
    ).toBe('Sitemap: https://a.example/s.xml');
  });

  it('gives none where no sitemap is named, or for another check', () => {
    expect(sitemapLine({ id: 'robots-txt-no-sitemap' }, 'It has no Sitemap: line')).toBeNull();
    expect(sitemapLine({ id: 'sitemap-missing' }, 'found at https://a.example/s.xml')).toBeNull();
  });
});

describe('a title or description cut in search results', () => {
  const description =
    'Wide selection of fantastic affordable garden sheds and storage boxes. Choose from plastic, metal and wooden sheds and storage boxes to help keep your garden clutter free.';

  it('splits it where Google cuts it, and says its length and how far over it is', () => {
    expect(
      cutTextOf(
        { id: 'description-too-long' },
        {
          description,
          descriptionInResults:
            'Wide selection of fantastic affordable garden sheds and storage boxes. Choose from plastic, metal and wooden sheds and storage boxes to help keep your…',
        },
      ),
    ).toEqual({
      shown:
        'Wide selection of fantastic affordable garden sheds and storage boxes. Choose from plastic, metal and wooden sheds and storage boxes to help keep your',
      cut: ' garden clutter free.',
      words: '171 characters: about 20 more than Google shows.',
    });
    expect(
      cutTextOf({ id: 'title-too-long' }, { title: 'Sofas and chairs | Example', titleInResults: 'Sofas and chairs…' }),
    ).toMatchObject({ shown: 'Sofas and chairs', cut: ' | Example' });
  });

  it('is nothing where nothing is cut, where the two don’t match, and for any other check', () => {
    expect(cutTextOf({ id: 'title-too-long' }, { title: 'Sofas', titleInResults: 'Sofas' })).toBeNull();
    expect(cutTextOf({ id: 'title-too-long' }, { title: 'Sofas', titleInResults: 'Chairs…' })).toBeNull();
    expect(cutTextOf({ id: 'title-too-long' }, { title: null, titleInResults: 'Sofas…' })).toBeNull();
    expect(cutTextOf({ id: 'title-duplicate' }, { title: 'Sofas and more', titleInResults: 'Sofas…' })).toBeNull();
  });
});
