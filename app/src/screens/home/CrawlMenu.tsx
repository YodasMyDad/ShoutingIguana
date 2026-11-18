import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import {
  ChevronRight,
  FolderOpen,
  GitCompareArrows,
  MoreHorizontal,
  Play,
  RefreshCw,
  RotateCcw,
  Trash2,
  X,
} from 'lucide-react';
import { useState } from 'react';
import { queryKeys } from '../../api/queries';
import type { CrawlListItem, CrawlStatus } from '../../api/types';
import { analyseCrawl, continueCrawl } from '../../crawl/actions';
import { notifyError } from '../../feedback/notify';
import { isTauri } from '../../lib/platform';
import { setCompareChoice } from '../../settings/compareWith';
import { showInFolder } from '../../shell/os';
import { ConfirmDialog } from '../../ui/Dialog';
import { IconButton } from '../../ui/IconButton';
import { DropdownMenu, MenuItem, MenuSeparator } from '../../ui/Menu';
import { analyseWords, compareAction, crawlActions } from './library';
import { crawlLabel, deleteCrawlFile, deleteQuestion, removeFromLibrary } from './libraryActions';

/**
 * Every action of a crawl in the library (docs/v2-ui-ia.md, Home): Open, Continue, Crawl again, Analyse again, Compare
 * with… (the fix loop: the site's latest crawl's Overview, with this crawl picked), Show in folder, Remove from library
 * (with Undo) and Delete file…, which asks first in a dialog that names the crawl and its file. An action the crawl's
 * state doesn't allow is there but disabled, so the menu keeps its shape.
 */
export function CrawlMenu({
  item,
  status,
  older = false,
  site,
}: {
  item: CrawlListItem;
  status: CrawlStatus;
  /** An older crawl of its site, folded under the latest: its name says when it ran. */
  older?: boolean;
  /** The site's latest crawl in the table, and how many crawls of the site it has: for Compare with…. */
  site: { latest: CrawlListItem; crawls: number };
}) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [confirming, setConfirming] = useState(false);
  // The crawl the confirmation was opened for, and its name then: the row can show another crawl while it's open (its
  // site's running crawl finishes and becomes the latest), and Delete file deletes what the dialog named.
  const [target, setTarget] = useState({ item, label: crawlLabel(item, older) });
  const actions = crawlActions(item, status);
  const label = crawlLabel(item, older);
  const refresh = () => queryClient.invalidateQueries({ queryKey: queryKeys.library, exact: true });
  const compare = compareAction(item, status, site.latest, site.crawls);

  return (
    <>
      <DropdownMenu align="end" trigger={<IconButton label={`Actions for ${label}`} icon={MoreHorizontal} size="sm" />}>
        <MenuItem
          icon={ChevronRight}
          disabled={!actions.canOpen}
          onSelect={() => void navigate({ to: '/crawls/$crawl', params: { crawl: item.id } })}
        >
          Open
        </MenuItem>
        {actions.canContinue ? (
          <MenuItem icon={Play} onSelect={() => void continueCrawl(item.id)}>
            Continue
          </MenuItem>
        ) : null}
        <MenuItem
          icon={RotateCcw}
          disabled={!actions.canCrawlAgain}
          onSelect={() => void navigate({ to: '/new', search: { from: item.id } })}
        >
          Crawl again…
        </MenuItem>
        <MenuItem icon={RefreshCw} disabled={!actions.canAnalyse} onSelect={() => void analyseCrawl(item.id)}>
          {analyseWords(item, status)}
        </MenuItem>
        <MenuItem
          icon={GitCompareArrows}
          disabled={!compare.enabled}
          detail={compare.why}
          onSelect={() => {
            if (compare.choice !== null) setCompareChoice(compare.latestId, compare.choice);
            // The latest crawl itself: its Overview with Compare with… open, to pick there.
            void navigate({
              to: '/crawls/$crawl/overview',
              params: { crawl: compare.latestId },
              search: compare.choice === null ? { compare: 1 } : {},
            });
          }}
        >
          Compare with…
        </MenuItem>
        {isTauri ? (
          <MenuItem
            icon={FolderOpen}
            disabled={item.missing}
            onSelect={() => void showInFolder(item.id).catch((error: unknown) => notifyError(error))}
          >
            Show in folder
          </MenuItem>
        ) : null}
        <MenuSeparator />
        <MenuItem icon={X} disabled={!actions.canRemove} onSelect={() => void removeFromLibrary(item, refresh)}>
          Remove from library
        </MenuItem>
        <MenuItem
          icon={Trash2}
          destructive
          disabled={!actions.canDelete}
          onSelect={() => {
            setTarget({ item, label });
            setConfirming(true);
          }}
        >
          Delete file…
        </MenuItem>
      </DropdownMenu>
      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title={deleteQuestion(target.item, older)}
        description={`The file ${target.item.fileName} is deleted from the disk, with everything the crawl found and its analysis. This can’t be undone.`}
        confirmLabel="Delete file"
        destructive
        errorTitle="The file couldn’t be deleted."
        onConfirm={() => deleteCrawlFile(target.item, refresh)}
      />
    </>
  );
}
