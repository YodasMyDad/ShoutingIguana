import { useParams } from '@tanstack/react-router';
import { ScreenStub } from './ScreenStub';

/** Issues: the checks by category, the selected check explained, its rows grouped by detail. Its own slice. */
export function IssuesScreen() {
  const { check } = useParams({ from: '/crawls/$crawl/issues/{-$check}' });
  return (
    <ScreenStub
      title="Issues"
      purpose="Every check by category, with severity and pages affected. The selected check says what’s wrong, why it matters and how to fix it, and groups its rows by detail, so a template fault is one row."
    >
      {check ? <p className="text-13 text-text-muted">Selected check: {check}</p> : null}
    </ScreenStub>
  );
}
