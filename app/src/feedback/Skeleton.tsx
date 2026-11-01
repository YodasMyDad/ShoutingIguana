import clsx from 'clsx';

/**
 * A placeholder block with the shape of the content it stands for. It shimmers only when motion is allowed; under
 * reduced motion it is a still block (base.css). Hidden from screen readers: the region it fills says it's busy.
 */
export function Skeleton({ className }: { className?: string }) {
  return <span aria-hidden="true" className={clsx('skeleton block', className)} />;
}

/** Rows of text-sized skeletons, for lists before their first page arrives. */
export function SkeletonRows({ rows, label }: { rows: number; label: string }) {
  return (
    <div role="status" aria-busy="true" aria-label={label} className="flex flex-col gap-3 py-2">
      {Array.from({ length: rows }, (_, index) => (
        <div key={index} className="flex items-center gap-4">
          <Skeleton className="h-(--skeleton-text) w-1/3" />
          <Skeleton className="h-(--skeleton-text) w-1/6" />
          <Skeleton className="h-(--skeleton-text) w-1/12" />
        </div>
      ))}
    </div>
  );
}
