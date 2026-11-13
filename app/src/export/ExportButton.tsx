import { Download } from 'lucide-react';
import { commands } from '../keyboard/registry';
import { Button } from '../ui/Button';
import { EXPORT_COMMAND } from './useExportCommand';

/** The Export button in a screen's header: the command's, with its key in the tooltip. */
export function ExportButton({ disabled }: { disabled?: boolean | undefined }) {
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
