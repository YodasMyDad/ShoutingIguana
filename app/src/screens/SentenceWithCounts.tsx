import { TextLink } from '../ui/TextLink';
import type { SentencePart } from './countSentence';

/** A sentence whose counts of URLs open exactly those URLs in the crawl's URL grid. */
export function CountSentence({ crawlId, parts }: { crawlId: string; parts: readonly SentencePart[] }) {
  return (
    <>
      {parts.map((part, index) =>
        typeof part === 'string' ? (
          part
        ) : (
          <TextLink
            key={index}
            to="/crawls/$crawl/urls"
            params={{ crawl: crawlId }}
            search={{ filter: [...part.filters] }}
            variant="inline"
            className="figures"
          >
            {part.text}
          </TextLink>
        ),
      )}
    </>
  );
}
