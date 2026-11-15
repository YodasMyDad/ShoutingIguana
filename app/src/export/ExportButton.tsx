import { Download } from 'lucide-react';
import { commands } from '../keyboard/registry';
import { Button } from '../ui/Button';
import { IconButton } from '../ui/IconButton';
import { EXPORT_COMMAND } from './useExportCommand';

/** The Export button in a screen's header: the command's, with its key in the tooltip. */
export function ExportButton({
  disabled,
  compact = false,
}: {
  disabled?: boolean | undefined;
  /** Its icon alone, named Export: a short screen's folded header (`useShortScreen`). */
  compact?: boolean | undefined;
}) {
  if (compact) {
    return (
      <IconButton
        label="Export"
        icon={Download}
        size="sm"
        command={EXPORT_COMMAND}
        disabled={disabled}
        onClick={() => void commands.run(EXPORT_COMMAND)}
      />
    );
  }
  return (
    <Button
      variant="ghost"
      size="sm"
      icon={Download}
      command={EXPORT_COMMAND}
      disabled={disabled}
      onClick={() => void commands.run(EXPORT_COMMAND)}
    >
      Export
    </Button>
  );
}
