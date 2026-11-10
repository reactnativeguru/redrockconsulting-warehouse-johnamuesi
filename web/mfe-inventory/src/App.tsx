import { notify, type RemoteAppProps } from '@basecamp/mfe-contracts';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { createInventoryApi, type StockRow } from './api';

/**
 * Store stock dashboard. Re-loads whenever the shell changes the selected store
 * (storeId prop) - that's the whole shell -> remote "API".
 */
export default function InventoryApp({ apiBaseUrl, storeId }: RemoteAppProps) {
  const api = useMemo(() => createInventoryApi(apiBaseUrl), [apiBaseUrl]);
  const [rows, setRows] = useState<StockRow[]>([]);
  const [lowOnly, setLowOnly] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await api.getStock(storeId, lowOnly));
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [api, storeId, lowOnly]);

  useEffect(() => {
    void load();
  }, [load]);

  async function writeOffOne(row: StockRow) {
    try {
      const updated = await api.adjust(storeId, row.sku, -1, 'Damaged - written off via admin portal');
      setRows((rs) => rs.map((r) => (r.sku === row.sku ? updated : r)));
      notify({ level: 'info', message: `Wrote off 1 × ${row.sku} in ${storeId}` });
    } catch (e) {
      setError((e as Error).message);
    }
  }

  const lowCount = rows.filter((r) => r.isLow).length;

  return (
    <section>
      <h2 style={{ marginTop: 0 }}>Stock - {storeId}</h2>
      <label style={{ display: 'block', marginBottom: 12 }}>
        <input type="checkbox" checked={lowOnly} onChange={(e) => setLowOnly(e.target.checked)} /> Low stock only
      </label>
      {error && <p role="alert" style={{ color: '#b3261e' }}>{error}</p>}
      {!loading && <p>{rows.length} lines · <strong>{lowCount} low</strong></p>}
      <table style={{ width: '100%', borderCollapse: 'collapse', background: 'white' }}>
        <thead>
          <tr style={{ textAlign: 'left' }}>
            <th>SKU</th><th>On hand</th><th>Reserved</th><th>Available</th><th>Reorder at</th><th>Last counted</th><th />
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.sku} data-testid={`row-${r.sku}`} style={{ borderTop: '1px solid #ddd', background: r.isLow ? '#fdecea' : undefined }}>
              <td>{r.sku}</td>
              <td>{r.onHand}</td>
              <td>{r.reserved}</td>
              <td>{r.available}{r.isLow && ' ⚠'}</td>
              <td>{r.reorderThreshold}</td>
              <td>{r.lastCountedAt ? new Date(r.lastCountedAt).toLocaleString() : '-'}</td>
              <td><button onClick={() => void writeOffOne(r)}>Write off 1</button></td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
