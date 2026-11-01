import { Toaster } from 'sonner';
import { resolvedTheme } from '../settings/appearance';
import { useSettings } from '../settings/settingsStore';
import { TOAST_DURATION_MS } from './notify';

/** The one toaster: bottom right, three at a time, reachable from the keyboard with Sonner's Alt+T. */
export function AppToaster() {
  const theme = useSettings(resolvedTheme);
  return (
    <Toaster
      position="bottom-right"
      theme={theme}
      visibleToasts={3}
      duration={TOAST_DURATION_MS}
      closeButton
      containerAriaLabel="Notifications"
      toastOptions={{
        classNames: {
          toast: 'si-toast',
          title: 'si-toast-title',
          description: 'si-toast-description',
          actionButton: 'si-toast-action',
          cancelButton: 'si-toast-cancel',
          closeButton: 'si-toast-close',
        },
      }}
    />
  );
}
