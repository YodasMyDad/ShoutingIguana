import { ScreenStub } from './ScreenStub';

/** robots.txt: each origin's file with line numbers, and the blocked URLs with the rule that matched. */
export function RobotsScreen() {
  return (
    <ScreenStub
      title="robots.txt"
      purpose="Each origin’s file, with line numbers, and the blocked URLs, each with the rule that matched it. It needs E3’s /robots."
    />
  );
}
