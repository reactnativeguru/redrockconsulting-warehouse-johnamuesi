import { NOTIFY_EVENT } from '@basecamp/mfe-contracts';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import CatalogApp from './App';

const products = [
  { sku: 'BC-HAT-001', name: 'Thermal Beanie', category: 'Accessories', price: 9.99, barcode: '5060000000103' },
  { sku: 'BC-JKT-001', name: 'Ridgeline Waterproof Jacket', category: 'Jackets', price: 89.99, barcode: '5060000000011' },
];

function mockFetch() {
  const fetchMock = jest.fn(async (url: string, init?: RequestInit) => {
    if (init?.method === 'PUT') return { ok: true, status: 204, json: async () => undefined };
    const search = new URL(url).searchParams.get('search') ?? '';
    const items = products.filter((p) => p.name.toLowerCase().includes(search.toLowerCase()));
    return { ok: true, status: 200, json: async () => ({ items, total: items.length, page: 1, pageSize: 20 }) };
  });
  global.fetch = fetchMock as unknown as typeof fetch;
  return fetchMock;
}

describe('Catalog micro-frontend', () => {
  it('lists products and filters by search', async () => {
    mockFetch();
    const user = userEvent.setup();
    render(<CatalogApp apiBaseUrl="http://api" storeId="STORE-001" />);

    expect(await screen.findByText('Thermal Beanie')).toBeInTheDocument();
    expect(screen.getByText('Ridgeline Waterproof Jacket')).toBeInTheDocument();

    await user.type(screen.getByLabelText('Search products'), 'jacket');
    await user.click(screen.getByRole('button', { name: 'Search' }));

    expect(await screen.findByText('Ridgeline Waterproof Jacket')).toBeInTheDocument();
    expect(screen.queryByText('Thermal Beanie')).not.toBeInTheDocument();
  });

  it('changes a price via PUT and notifies the shell', async () => {
    const fetchMock = mockFetch();
    const notified = jest.fn();
    window.addEventListener(NOTIFY_EVENT, (e) => notified((e as CustomEvent).detail));
    const user = userEvent.setup();
    render(<CatalogApp apiBaseUrl="http://api" storeId="STORE-001" />);

    await screen.findByText('Thermal Beanie');
    await user.click(screen.getAllByRole('button', { name: 'Change price' })[0]);
    const input = screen.getByLabelText('New price for BC-HAT-001');
    await user.clear(input);
    await user.type(input, '7.99');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('£7.99')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      'http://api/api/products/BC-HAT-001/price',
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ price: 7.99 }) }),
    );
    expect(notified).toHaveBeenCalledWith(expect.objectContaining({ level: 'success' }));
  });
});
