import { Skeleton } from '../../feedback/Skeleton';
import { Card } from '../../ui/Card';

function Rows({ count }: { count: number }) {
  return (
    <>
      {Array.from({ length: count }, (_, index) => (
        <div key={index} className="flex items-center gap-3 py-1.5">
          <Skeleton className="size-(--icon-md)" />
          <Skeleton className="h-(--skeleton-text) flex-1" />
          <Skeleton className="h-(--skeleton-text) w-1/12" />
        </div>
      ))}
    </>
  );
}

/**
 * The first load: the answer's own layout in skeletons (the severity figures, Fix first beside the categories), so
 * nothing moves when the data arrives. `visible` follows the loading rule: nothing for the first 150 ms, then at least
 * 300 ms.
 */
export function OverviewSkeleton({ visible }: { visible: boolean }) {
  return (
    <div aria-busy="true" aria-label="Loading the overview" role="status" className="flex flex-col gap-4">
      <Card className="grid grid-cols-4 @max-split:grid-cols-2">
        {Array.from({ length: 4 }, (_, index) => (
          <div key={index} className="flex min-h-(--overview-figure-min) flex-col gap-3 px-6 py-5">
            {visible ? (
              <>
                <Skeleton className="h-(--skeleton-text) w-1/3" />
                <Skeleton className="h-(--skeleton-figure) w-(--skeleton-figure-width)" />
                <Skeleton className="h-(--skeleton-text) w-1/2" />
              </>
            ) : null}
          </div>
        ))}
      </Card>
      <div className="grid grid-cols-(--overview-columns) items-start gap-4 @max-split:grid-cols-1">
        <Card className="flex min-h-(--overview-list-min) flex-col gap-2 p-5">
          {visible ? (
            <>
              <Skeleton className="h-(--skeleton-title) w-1/4" />
              <Rows count={8} />
            </>
          ) : null}
        </Card>
        <Card className="flex min-h-(--overview-list-min) flex-col gap-2 p-5">
          {visible ? (
            <>
              <Skeleton className="h-(--skeleton-title) w-1/3" />
              <Rows count={6} />
            </>
          ) : null}
        </Card>
      </div>
    </div>
  );
}
