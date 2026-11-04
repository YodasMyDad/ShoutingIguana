import { TextLink } from '../ui/TextLink';
import { ScreenStub } from './ScreenStub';

export function NotFoundScreen() {
  return (
    <ScreenStub title="Nothing here" purpose="This view doesn’t exist, or the link that led here is out of date.">
      <TextLink to="/" search={{}} className="self-start text-14">
        Go to Home
      </TextLink>
    </ScreenStub>
  );
}
