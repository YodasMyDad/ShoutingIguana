import { ChevronRight, ExternalLink } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import type { LicencesState, LicensedPackage } from './licenceList';
import { formatCount, formatCountOf } from '../../../lib/format';
import { Button } from '../../../ui/Button';
import { Icon } from '../../../ui/Icon';
import { SearchField } from '../../../ui/SearchField';
import { openInBrowser } from '../../../shell/os';

const SOURCE_WORDS: Record<LicensedPackage['source'], string> = {
  npm: 'the window (npm)',
  cargo: 'the app (Rust)',
  nuget: 'the engine (.NET)',
  font: 'fonts',
};

/**
 * About's third-party licences (gap 31): every package the app ships, with its licence and its licence's text, as the
 * build found them in the npm, Cargo and NuGet packages actually in it. Collapsed to a line until asked for.
 */
export function Licences({
  state,
  defaultOpen = false,
}: {
  state: LicencesState;
  /** For stories: the list shown from the start. */
  defaultOpen?: boolean;
}) {
  const [open, setOpen] = useState(defaultOpen);
  const [query, setQuery] = useState('');
  const listId = useId();
  const shown = useMemo(() => {
    if (state.kind !== 'ready') return [];
    const words = query.trim().toLowerCase();
    return words === ''
      ? state.list.packages
      : state.list.packages.filter((pkg) => `${pkg.name} ${pkg.licence}`.toLowerCase().includes(words));
  }, [state, query]);

  if (state.kind === 'loading') return <p className="text-13 text-text-secondary">Reading the list…</p>;
  if (state.kind === 'missing') {
    return (
      <p className="text-13 text-text-secondary">
        The list of licences is made when the app is built, so this copy, run from its source, has none.
      </p>
    );
  }
  if (state.kind === 'failed') {
    return <p className="text-13 text-text-secondary">The list of licences couldn’t be read.</p>;
  }
  const { packages } = state.list;
  const bySource = (['npm', 'cargo', 'nuget', 'font'] as const)
    .map((source) => ({ source, count: packages.filter((pkg) => pkg.source === source).length }))
    .filter(({ count }) => count > 0);
  return (
    <div className="flex w-full flex-col items-start gap-3">
      <p className="text-13 text-text-secondary">
        {formatCountOf(packages.length, 'package')}:{' '}
        {bySource.map(({ source, count }) => `${formatCount(count)} in ${SOURCE_WORDS[source]}`).join(', ')}.
        {state.list.complete ? null : ' This build couldn’t read every source, so the list may be short.'}
      </p>
      <Button size="sm" aria-expanded={open} aria-controls={listId} onClick={() => setOpen((was) => !was)}>
        {open ? 'Hide the licences' : 'Show the licences'}
      </Button>
      {open ? (
        <div id={listId} className="flex w-full flex-col gap-2">
          <SearchField
            label="Find a package or a licence"
            placeholder="Find a package or a licence"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            onClear={() => setQuery('')}
          />
          <ul
            aria-label="Third-party licences"
            className="max-h-(--licence-list-height) w-full overflow-auto rounded-sm border border-border-subtle"
          >
            {shown.map((pkg) => (
              <LicenceRow
                key={`${pkg.source}:${pkg.name}@${pkg.version}`}
                pkg={pkg}
                text={pkg.text === null ? null : (state.list.texts[pkg.text] ?? null)}
              />
            ))}
            {shown.length === 0 ? (
              <li className="px-3 py-2 text-13 text-text-secondary">No package or licence has those words.</li>
            ) : null}
          </ul>
        </div>
      ) : null}
    </div>
  );
}

function LicenceRow({ pkg, text }: { pkg: LicensedPackage; text: string | null }) {
  const [open, setOpen] = useState(false);
  const textId = useId();
  return (
    <li className="border-b border-border-subtle last:border-b-0">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-1.5 text-13">
        {text ? (
          <button
            type="button"
            aria-expanded={open}
            aria-controls={textId}
            onClick={() => setOpen((was) => !was)}
            className="flex min-w-0 items-center gap-1.5 rounded-sm text-left font-medium hover:text-accent-text"
          >
            <Icon icon={ChevronRight} size="sm" className={open ? 'rotate-90' : undefined} />
            <span className="break-all">{pkg.name}</span>
          </button>
        ) : (
          <span className="min-w-0 pl-5 font-medium break-all">{pkg.name}</span>
        )}
        {pkg.version ? <span className="font-mono text-12 text-text-secondary">{pkg.version}</span> : null}
        <span className="text-text-secondary">{pkg.licence}</span>
        {pkg.url ? (
          <a
            href={pkg.url}
            onClick={(event) => {
              event.preventDefault();
              if (pkg.url) void openInBrowser(pkg.url);
            }}
            className="si-link ml-auto inline-flex items-center gap-1 text-12"
          >
            Its page
            <span className="sr-only">: {pkg.name}, in your browser</span>
            <Icon icon={ExternalLink} size="sm" />
          </a>
        ) : null}
      </div>
      {open && text ? (
        <pre
          id={textId}
          className="mx-3 mb-2 max-h-(--update-notes-max) overflow-auto rounded-sm bg-surface-sunken p-2 font-mono text-12 whitespace-pre-wrap text-text-secondary"
        >
          {text}
        </pre>
      ) : null}
    </li>
  );
}
