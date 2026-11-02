import { Component, type ErrorInfo, type ReactNode } from 'react';
import { ErrorState } from './ErrorState';

interface RegionBoundaryProps {
  /** The part of the shell, in words: "the sidebar", "the top bar". */
  name: string;
  children: ReactNode;
}

interface RegionBoundaryState {
  failed: boolean;
  error: unknown;
}

/**
 * An error boundary for one part of the shell: the sidebar, the top bar, the inspector. A part that fails says so in
 * its own place, with Try again, and the rest of the app keeps working. Screens have their routes' own boundaries.
 */
export class RegionBoundary extends Component<RegionBoundaryProps, RegionBoundaryState> {
  override state: RegionBoundaryState = { failed: false, error: null };

  static getDerivedStateFromError(error: unknown): RegionBoundaryState {
    return { failed: true, error };
  }

  override componentDidCatch(error: unknown, info: ErrorInfo): void {
    console.error(`The ${this.props.name} failed`, error, info.componentStack);
  }

  override render(): ReactNode {
    if (!this.state.failed) return this.props.children;
    return (
      <div className="shrink-0 border-border bg-surface">
        <ErrorState
          compact
          error={this.state.error}
          title={`The ${this.props.name} couldn’t be shown.`}
          onRetry={() => this.setState({ failed: false, error: null })}
        />
      </div>
    );
  }
}
