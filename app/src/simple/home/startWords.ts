// What Home says when the engine won't start a check (docs/v2-ui-simple.md, Home), in Simple's words: never the
// engine's own ("Start URL must be an absolute http or https URL"). Pure.

/**
 * The address field's error for the engine's `startUrl` problems. It has two, told apart by the address itself: one
 * with a user name and password in it, and one that isn't a web address.
 */
export function startUrlWords(address: string): string {
  if (hasCredentials(address)) {
    return 'Leave out the user name and password: they would be kept in the check’s file. A site that asks for a password can be checked in Advanced.';
  }
  return 'That isn’t a web address. Type one such as www.example.co.uk.';
}

function hasCredentials(address: string): boolean {
  try {
    const url = new URL(address);
    return url.username !== '' || url.password !== '';
  } catch {
    return false;
  }
}
