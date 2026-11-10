/**
 * The ONLY things the shell and the micro-frontends agree on.
 *
 * Micro-frontend rule of thumb: share as little as possible. Teams own their
 * remotes end-to-end; the contract is (1) the props the shell passes in and
 * (2) browser events for cross-app communication. No shared Redux store,
 * no reaching into each other's components.
 */

/** Props every remote's default-exported App component receives from the shell. */
export interface RemoteAppProps {
  /** Base URL of the API gateway. */
  apiBaseUrl: string;
  /** Store currently selected in the shell header. */
  storeId: string;
}

/** Remotes -> shell: "please show a notification". */
export const NOTIFY_EVENT = 'basecamp:notify';

export interface NotifyDetail {
  message: string;
  level: 'info' | 'success' | 'error';
}

export function notify(detail: NotifyDetail): void {
  window.dispatchEvent(new CustomEvent<NotifyDetail>(NOTIFY_EVENT, { detail }));
}

export function onNotify(handler: (detail: NotifyDetail) => void): () => void {
  const listener = (e: Event) => handler((e as CustomEvent<NotifyDetail>).detail);
  window.addEventListener(NOTIFY_EVENT, listener);
  return () => window.removeEventListener(NOTIFY_EVENT, listener);
}
