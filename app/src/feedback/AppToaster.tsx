import { Toaster } from 'sonner';
import { useSettings } from '../settings/settingsStore';
import { TOAST_DURATION_MS } from './notify';

/**
 * The one toaster: bottom right, three at a time, reachable from the keyboard with Sonner's Alt+T. Unstyled, so every
 * look is the app's (base.css): its tokens, its focus rings, and its theme, the OS's while the setting says "system",
 * however often the OS changes it.
 */
export function AppToaster() {
  const theme = useSettings((settings) => settings.theme);
  return (
    <Toaster
      position="bottom-right"
      theme={theme}
      visibleToasts={3}
      duration={TOAST_DURATION_MS}
      closeButton
      containerAriaLabel="Notifications"
      toastOptions={{
        unstyled: true,
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
