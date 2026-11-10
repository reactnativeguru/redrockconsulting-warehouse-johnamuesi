import { notify, type RemoteAppProps } from '@basecamp/mfe-contracts';
import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Shell, type RemoteRegistry } from './Shell';

// Fake remotes: the shell is tested in isolation from the real micro-frontends.
const FakeCatalog = ({ storeId }: RemoteAppProps) => <p>catalog for {storeId}</p>;
const FakeInventory = ({ storeId }: RemoteAppProps) => <p>inventory for {storeId}</p>;
const Broken = () => {
  throw new Error('remote failed to load');
};

beforeEach(() => {
  global.fetch = jest.fn(async () => ({ ok: true, json: async () => ['STORE-001', 'STORE-002'] })) as unknown as typeof fetch;
  jest.spyOn(console, 'error').mockImplementation(() => {});
});

const registry = (overrides: Partial<RemoteRegistry> = {}): RemoteRegistry => ({
  catalog: { label: 'Catalogue', component: FakeCatalog },
  inventory: { label: 'Stock', component: FakeInventory },
  ...overrides,
});

describe('Shell', () => {
  it('navigates between micro-frontends and passes the selected store down', async () => {
    const user = userEvent.setup();
    render(<Shell remotes={registry()} apiBaseUrl="http://api" />);

    expect(screen.getByText('catalog for STORE-001')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Stock' }));
    expect(screen.getByText('inventory for STORE-001')).toBeInTheDocument();

    await screen.findByRole('option', { name: 'STORE-002' });
    await user.selectOptions(screen.getByLabelText('Store'), 'STORE-002');
    expect(screen.getByText('inventory for STORE-002')).toBeInTheDocument();
  });

  it('isolates a failing remote without taking down the shell', async () => {
    const user = userEvent.setup();
    render(<Shell remotes={registry({ inventory: { label: 'Stock', component: Broken } })} apiBaseUrl="http://api" />);

    await user.click(screen.getByRole('button', { name: 'Stock' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Stock is unavailable right now.');

    // The rest of the portal still works.
    await user.click(screen.getByRole('button', { name: 'Catalogue' }));
    expect(screen.getByText('catalog for STORE-001')).toBeInTheDocument();
  });

  it('shows notifications raised by remotes', async () => {
    render(<Shell remotes={registry()} apiBaseUrl="http://api" />);

    act(() => notify({ level: 'success', message: 'Price updated' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Price updated');
  });
});
