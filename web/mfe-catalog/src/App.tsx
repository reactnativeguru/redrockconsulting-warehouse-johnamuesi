import { notify, type RemoteAppProps } from '@basecamp/mfe-contracts';
import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { createCatalogApi, type ProductSummary } from './api';

/**
 * Product search + price editing for head-office / store managers.
 * Price changes flow: this UI -> Catalog API -> ProductPriceChanged event ->
 * ePOS ETL -> price file -> tills.
 */
export default function CatalogApp({ apiBaseUrl }: RemoteAppProps) {
  const api = useMemo(() => createCatalogApi(apiBaseUrl), [apiBaseUrl]);
  const [search, setSearch] = useState('');
  const [products, setProducts] = useState<ProductSummary[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<string | null>(null);
  const [draftPrice, setDraftPrice] = useState('');

  async function load(term: string) {
    try {
      const result = await api.search(term);
      setProducts(result.items);
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  useEffect(() => {
    void load('');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api]);

  function onSearch(e: FormEvent) {
    e.preventDefault();
    void load(search);
  }

  async function savePrice(product: ProductSummary) {
    const price = Number(draftPrice);
    if (!(price > 0)) {
      setError('Enter a valid price');
      return;
    }
    try {
      await api.changePrice(product.sku, price);
      setProducts((ps) => ps.map((p) => (p.sku === product.sku ? { ...p, price } : p)));
      setEditing(null);
      notify({ level: 'success', message: `${product.name} now £${price.toFixed(2)} - sent to tills` });
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <section>
      <h2 style={{ marginTop: 0 }}>Product catalogue</h2>
      <form onSubmit={onSearch} style={{ display: 'flex', gap: 8, marginBottom: 16 }}>
        <input
          aria-label="Search products"
          placeholder="Name, SKU or barcode"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          style={{ flex: 1, padding: 8 }}
        />
        <button type="submit">Search</button>
      </form>
      {error && <p role="alert" style={{ color: '#b3261e' }}>{error}</p>}
      <table style={{ width: '100%', borderCollapse: 'collapse', background: 'white' }}>
        <thead>
          <tr style={{ textAlign: 'left' }}>
            <th>SKU</th><th>Name</th><th>Category</th><th>Price</th><th />
          </tr>
        </thead>
        <tbody>
          {products.map((p) => (
            <tr key={p.sku} style={{ borderTop: '1px solid #ddd' }}>
              <td>{p.sku}</td>
              <td>{p.name}</td>
              <td>{p.category}</td>
              <td>
                {editing === p.sku ? (
                  <input
                    aria-label={`New price for ${p.sku}`}
                    value={draftPrice}
                    onChange={(e) => setDraftPrice(e.target.value)}
                    style={{ width: 80 }}
                  />
                ) : (
                  `£${p.price.toFixed(2)}`
                )}
              </td>
              <td>
                {editing === p.sku ? (
                  <>
                    <button onClick={() => void savePrice(p)}>Save</button>{' '}
                    <button onClick={() => setEditing(null)}>Cancel</button>
                  </>
                ) : (
                  <button
                    onClick={() => {
                      setEditing(p.sku);
                      setDraftPrice(p.price.toFixed(2));
                    }}
                  >
                    Change price
                  </button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
