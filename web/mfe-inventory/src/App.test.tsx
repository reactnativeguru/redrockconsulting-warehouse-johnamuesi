import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import InventoryApp from './App';
import type { StockRow } from './api';

const row = (sku: string, available: number, isLow: boolean): StockRow => ({
  sku, onHand: available, reserved: 0, available, reorderThreshold: 3, isLow, lastCountedAt: null,
});

function mockFetch(rowsByStore: Record<string, StockRow[]>) {
  const fetchMock = jest.fn(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST') {
      return { ok: true, status: 200, json: async () => row('BC-JKT-001', 4, false) };
    }
    const u = new URL(url);
    const storeId = decodeURIComponent(u.pathname.split('/')[4]);
    const lowOnly = u.searchParams.get('lowOnly') === 'true';
    const rows = (rowsByStore[storeId] ?? []).filter((r) => !lowOnly || r.isLow);
    return { ok: true, status: 200, json: async () => rows };
  });
  global.fetch = fetchMock as unknown as typeof fetch;
  return fetchMock;
}

describe('Inventory micro-frontend', () => {
  const data = {
    'STORE-001': [row('BC-JKT-001', 5, false), row('BC-TENT-001', 1, true)],
    'STORE-002': [row('BC-HAT-001', 0, true)],
  };

  it('shows stock for the store selected in the shell and reloads when it changes', async () => {
    mockFetch(data);
    const { rerender } = render(<InventoryApp apiBaseUrl="http://api" storeId="STORE-001" />);

    expect(await screen.findByText('BC-TENT-001')).toBeInTheDocument();
    expect(screen.getByText('1 low')).toBeInTheDocument();

    rerender(<InventoryApp apiBaseUrl="http://api" storeId="STORE-002" />);

    expect(await screen.findByText('BC-HAT-001')).toBeInTheDocument();
    expect(screen.queryByText('BC-TENT-001')).not.toBeInTheDocument();
  });

  it('filters to low stock', async () => {
    const fetchMock = mockFetch(data);
    const user = userEvent.setup();
    render(<InventoryApp apiBaseUrl="http://api" storeId="STORE-001" />);
    await screen.findByText('BC-JKT-001');

    await user.click(screen.getByLabelText('Low stock only'));

    expect(fetchMock).toHaveBeenLastCalledWith('http://api/api/inventory/stores/STORE-001/stock?lowOnly=true');
    await waitFor(() => expect(screen.queryByText('BC-JKT-001')).not.toBeInTheDocument());
    expect(screen.getByText('BC-TENT-001')).toBeInTheDocument();
  });

  it('writes off a unit and updates the row', async () => {
    mockFetch(data);
    const user = userEvent.setup();
    render(<InventoryApp apiBaseUrl="http://api" storeId="STORE-001" />);
    const jacketRow = await screen.findByTestId('row-BC-JKT-001');

    await user.click(within(jacketRow).getByRole('button', { name: 'Write off 1' }));

    expect(await within(screen.getByTestId('row-BC-JKT-001')).findAllByText('4')).toHaveLength(2);
  });
});
