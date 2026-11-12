import { useQuery } from '@tanstack/react-query';
import { checksQuery } from '../api/queries';
import { LOOKING_UP } from './filterSentence';

/**
 * The words for checks a view's filters name by their ids (the URL grid's `check`, a link's `from.check` and
 * `to.check`): each check's title from the catalog. Names, never ids: while the catalog is read, a placeholder; when
 * it can't be, plain words; a check this version doesn't have, plain words too. The catalog is read only when a
 * filter names a check (`named`).
 */
export function useCheckWords(named: boolean): (id: string | number) => string {
  const checks = useQuery({ ...checksQuery(), enabled: named });
  return (id) => {
    if (checks.data) {
      return checks.data.checks.find((check) => check.id === id)?.title ?? 'a check this version doesn’t have';
    }
    return checks.isError ? 'a check whose name couldn’t be read' : LOOKING_UP;
  };
}
