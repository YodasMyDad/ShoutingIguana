import { Skeleton } from '../../feedback/Skeleton';

/**
 * The report on its way: a blank sheet on the canvas where it will be, with the shape of its first page, so nothing
 * moves when it arrives. It should take well under a second, so the shapes show only past the skeleton's delay.
 */
export function ReportSheetSkeleton({ visible }: { visible: boolean }) {
  return (
    <div
      role="status"
      aria-busy="true"
      aria-label="Making the report"
      className="flex min-h-(--report-sheet-min) flex-1 justify-center overflow-hidden rounded-t-lg bg-surface-hover px-6 pt-6 inset-ring inset-ring-border"
    >
      <div className="flex h-full w-full max-w-(--paper-width) flex-col gap-4 rounded-t-xs bg-(--color-paper) p-12 shadow-2">
        {visible ? (
          <>
            <Skeleton className="h-(--skeleton-text) w-1/6" />
            <Skeleton className="h-(--skeleton-title) w-1/2" />
            <Skeleton className="h-(--skeleton-text) w-1/3" />
            <div className="mt-6 flex items-center gap-8">
              <Skeleton className="size-16 rounded-full" />
              <div className="flex flex-1 flex-col gap-3">
                <Skeleton className="h-(--skeleton-title) w-1/4" />
                <Skeleton className="h-(--skeleton-text) w-3/4" />
                <Skeleton className="h-(--skeleton-text) w-2/3" />
              </div>
            </div>
            {Array.from({ length: 5 }, (_, index) => (
              <div key={index} className="flex items-center gap-3 py-1">
                <Skeleton className="size-(--icon-md)" />
                <Skeleton className="h-(--skeleton-text) flex-1" />
                <Skeleton className="h-(--skeleton-text) w-1/12" />
              </div>
            ))}
          </>
        ) : null}
      </div>
    </div>
  );
}
