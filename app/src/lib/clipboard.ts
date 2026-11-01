/** Copies text, falling back to a hidden textarea where the async clipboard isn't allowed. */
export async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    const area = document.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    // The deprecated command is the only fallback there is when the clipboard API is refused.
    // eslint-disable-next-line @typescript-eslint/no-deprecated
    const copied = document.execCommand('copy');
    area.remove();
    return copied;
  }
}
