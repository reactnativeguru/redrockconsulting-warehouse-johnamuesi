import { Component, Suspense, type ReactNode } from 'react';

interface Props {
  name: string;
  children: ReactNode;
}

interface State {
  error: Error | null;
}

/**
 * Isolation: if a remote fails to load (its server is down, a bad deploy) or
 * throws while rendering, only THAT panel shows an error - the shell and the
 * other micro-frontends keep working. This is one of the main selling points
 * of micro-frontends, but only if you build the boundary.
 */
export class RemoteBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error) {
    // In production: send to App Insights / Sentry with the remote name.
    console.error(`[shell] remote "${this.props.name}" failed`, error);
  }

  render() {
    if (this.state.error) {
      return (
        <div role="alert" style={{ padding: 16, background: '#fdecea', borderRadius: 8 }}>
          <strong>{this.props.name} is unavailable right now.</strong>
          <div style={{ marginTop: 8 }}>
            <button onClick={() => this.setState({ error: null })}>Try again</button>
          </div>
        </div>
      );
    }
    return <Suspense fallback={<p>Loading {this.props.name}...</p>}>{this.props.children}</Suspense>;
  }
}
