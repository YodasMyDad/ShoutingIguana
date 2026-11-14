import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { ChevronRight, FolderOpen, MoreHorizontal, Play, RefreshCw, RotateCcw, Trash2, X } from 'lucide-react';
import { useState } from 'react';
import { api } from '../../api/client';
import { queryKeys } from '../../api/queries';
import type { CrawlListItem, CrawlStatus } from '../../api/types';
import { analyseCrawl, continueCrawl } from '../../crawl/actions';
import { notifyError } from '../../feedback/notify';
import { isTauri } from '../../lib/platform';
import { showInFolder } from '../../shell/os';
import { ConfirmDialog } from '../../ui/Dialog';
import { IconButton } from '../../ui/IconButton';
import { DropdownMenu, MenuItem, MenuSeparator } from '../../ui/Menu';
import { analyseWords, crawlActions } from './library';
import { crawlLabel, removeFromLibrary } from './libraryActions';

/**
 * Every action of a crawl in the library (docs/v2-ui-ia.md, Home): Open, Continue, Crawl again, Analyse again, Show in
 * folder, Remove from library (with Undo) and Delete file…, which asks first in a dialog that names the crawl and its
 * file. An action the crawl's state doesn't allow is there but disabled, so the menu keeps its shape.
 */
export function CrawlMenu({
  item,
  status,
  older = false,
}: {
  item: CrawlListItem;
  status: CrawlStatus;
  /** An older crawl of its site, folded under the latest: its name says when it ran. */
  older?: boolean;
}) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [confirming, setConfirming] = useState(false);
  const actions = crawlActions(item, status);
  const label = crawlLabel(item, older);
  const refresh = () => queryClient.invalidateQueries({ queryKey: queryKeys.library, exact: true });

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
        <MenuItem icon={Trash2} destructive disabled={!actions.canDelete} onSelect={() => setConfirming(true)}>
          Delete file…
        </MenuItem>
      </DropdownMenu>
      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title={`Delete ${label}’s crawl file?`}
        description={`The file ${item.fileName} is deleted from the disk, with everything the crawl found and its analysis. This can’t be undone.`}
        confirmLabel="Delete file"
        destructive
        errorTitle="The file couldn’t be deleted."
        onConfirm={async () => {
          await api.deleteCrawlFile(item.id);
          void refresh();
        }}
      />
    </>
  );
}
