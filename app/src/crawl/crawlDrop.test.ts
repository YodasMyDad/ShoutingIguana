import { describe, expect, it } from 'vitest';
import { dragWords, fileName, nextDragState, notCrawlFilesWords, splitDropped } from './crawlDrop';

describe('a drop on the window', () => {
  it('tells crawl files from the rest by their names, whatever the case', () => {
    expect(
      splitDropped(['C:\\Crawls\\garden.sicrawl', 'C:\\Crawls\\Notes.TXT', '/home/me/shop.SICRAWL', 'C:\\Crawls']),
    ).toEqual({
      crawls: ['C:\\Crawls\\garden.sicrawl', '/home/me/shop.SICRAWL'],
      others: ['C:\\Crawls\\Notes.TXT', 'C:\\Crawls'],
    });
    expect(fileName('C:\\Crawls\\garden.sicrawl')).toBe('garden.sicrawl');
    expect(fileName('/home/me/folder/')).toBe('folder');
  });

  it('says over the window what a drop will do, from the files dragged in, until they leave or land', () => {
    const none = { kind: 'none' } as const;
    const two = nextDragState({ type: 'enter', paths: ['a.sicrawl', 'b.sicrawl', 'c.txt'] }, none);
    expect(two).toEqual({ kind: 'crawls', count: 2 });
    expect(nextDragState({ type: 'over' }, two)).toBe(two);
    expect(nextDragState({ type: 'leave' }, two)).toEqual(none);
    expect(nextDragState({ type: 'drop', paths: ['a.sicrawl'] }, two)).toEqual(none);
    expect(nextDragState({ type: 'enter', paths: ['c.txt'] }, none)).toEqual({ kind: 'others' });

    expect(dragWords({ kind: 'crawls', count: 1 }, 'advanced')?.title).toBe('Drop to open the crawl file');
    expect(dragWords({ kind: 'crawls', count: 2 }, 'advanced')?.title).toBe('Drop to open the 2 crawl files');
    expect(dragWords({ kind: 'crawls', count: 1 }, 'simple')?.title).toBe('Drop to open the site check file');
    expect(dragWords({ kind: 'others' }, 'advanced')).toEqual({
      title: 'That isn’t a crawl file',
      detail: 'Shouting Iguana opens crawl files, whose names end in .sicrawl.',
    });
    expect(dragWords(none, 'simple')).toBeNull();
  });

  it('says which dropped files weren’t opened, and why, in each mode’s words', () => {
    expect(notCrawlFilesWords(['C:\\Crawls\\notes.txt'], 'advanced')).toEqual({
      title: 'notes.txt isn’t a crawl file, so it wasn’t opened.',
      description: 'Shouting Iguana opens crawl files, whose names end in .sicrawl.',
    });
    const many = notCrawlFilesWords(['a.txt', 'b.png', 'c.doc', 'd.pdf'], 'simple');
    expect(many.title).toBe('4 files aren’t site check files, so they weren’t opened.');
    expect(many.description).toBe(
      'Shouting Iguana opens site check files, whose names end in .sicrawl. Not opened: a.txt, b.png, c.doc….',
    );
  });
});
