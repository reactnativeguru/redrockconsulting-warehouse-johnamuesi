import { onNotify, type NotifyDetail, type RemoteAppProps } from '@basecamp/mfe-contracts';
import { useEffect, useState, type ComponentType } from 'react';
import { RemoteBoundary } from './RemoteBoundary';

export type RemoteRegistry = Record<string, { label: string; component: ComponentType<RemoteAppProps> }>;

interface ShellProps {
  remotes: RemoteRegistry;
  apiBaseUrl: string;
}

/**
 * The shell owns ONLY cross-cutting concerns: layout, navigation, which store is
 * selected, notifications (and in real life: auth). Business features live in
 * the remotes. Remotes are injected as a registry so the shell is testable
 * with fake remotes.
 */
export function Shell({ remotes, apiBaseUrl }: ShellProps) {
  const keys = Object.keys(remotes);
  const [active, setActive] = useState(keys[0]);
  const [stores, setStores] = useState<string[]>(['STORE-001']);
  const [storeId, setStoreId] = useState('STORE-001');
  const [toast, setToast] = useState<NotifyDetail | null>(null);

  useEffect(() => {
    fetch(`${apiBaseUrl}/api/inventory/stores`)
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(String(r.status)))))
      .then((list: string[]) => list.length && setStores(list))
      .catch(() => {
        /* keep default list - shell must render even if the API is down */
      });
  }, [apiBaseUrl]);

  // Remotes talk to the shell via browser events (see @basecamp/mfe-contracts).
  useEffect(() => onNotify(setToast), []);
  useEffect(() => {
    if (!toast) return;
    const t = setTimeout(() => setToast(null), 4000);
    return () => clearTimeout(t);
  }, [toast]);

  const Active = remotes[active].component;

  return (
    <div>
      <header style={{ background: '#1f5f4a', color: 'white', padding: '12px 24px', display: 'flex', alignItems: 'center', gap: 24 }}>
        <strong style={{ fontSize: 18 }}>Basecamp Store Admin</strong>
        <nav style={{ display: 'flex', gap: 8 }}>
          {keys.map((key) => (
            <button
              key={key}
              aria-current={key === active ? 'page' : undefined}
              onClick={() => setActive(key)}
              style={{ background: key === active ? 'white' : 'transparent', color: key === active ? '#1f5f4a' : 'white', border: '1px solid white', borderRadius: 4, padding: '6px 12px', cursor: 'pointer' }}
            >
              {remotes[key].label}
            </button>
          ))}
        </nav>
        <label style={{ marginLeft: 'auto' }}>
          Store{' '}
          <select aria-label="Store" value={storeId} onChange={(e) => setStoreId(e.target.value)}>
            {stores.map((s) => <option key={s}>{s}</option>)}
          </select>
        </label>
      </header>

      {toast && (
        <div role="status" style={{ background: toast.level === 'error' ? '#b3261e' : '#2e7d32', color: 'white', padding: '8px 24px' }}>
          {toast.message}
        </div>
      )}

      <main style={{ padding: 24 }}>
        {/* key={active} resets the error boundary when switching sections */}
        <RemoteBoundary key={active} name={remotes[active].label}>
          <Active apiBaseUrl={apiBaseUrl} storeId={storeId} />
        </RemoteBoundary>
      </main>
    </div>
  );
}
