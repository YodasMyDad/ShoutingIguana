import { Link } from '@tanstack/react-router';
import { ScreenStub } from './ScreenStub';

export function NotFoundScreen() {
  return (
    <ScreenStub title="Nothing here" purpose="This view doesn’t exist, or the link that led here is out of date.">
      <Link to="/" search={{}} className="text-14 text-accent-text underline underline-offset-(--underline-offset)">
        Go to Home
      </Link>
    </ScreenStub>
  );
}
