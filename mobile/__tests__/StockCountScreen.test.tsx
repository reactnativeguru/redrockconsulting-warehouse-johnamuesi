import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import React from 'react';
import { SyncStatusBar } from '../src/components/SyncStatusBar';
import { StockCountScreen } from '../src/screens/StockCountScreen';
import { buildServices } from './testUtils';

/**
 * React Native Testing Library: test what the colleague SEES and DOES
 * (type, press, read text) - not component internals.
 */
async function renderScreen(online: boolean) {
  const ctx = buildServices({ online });
  await render(
    <ctx.wrapper>
      <SyncStatusBar />
      <StockCountScreen />
    </ctx.wrapper>,
  );
  return ctx;
}

async function enterCount(sku: string, qty: string) {
  await fireEvent.changeText(screen.getByTestId('count-sku'), sku);
  await fireEvent.changeText(screen.getByTestId('count-quantity'), qty);
  await fireEvent.press(screen.getByRole('button', { name: 'Save count' }));
}

describe('StockCountScreen', () => {
  it('saves a count on the device while offline and shows it as waiting', async () => {
    const { store, api } = await renderScreen(false);

    await enterCount('bc-jkt-001', '12');

    expect(await screen.findByText('BC-JKT-001: 12')).toBeTruthy();
    expect(screen.getByText('Waiting to sync')).toBeTruthy();
    expect(screen.getByTestId('sync-status')).toHaveTextContent('Offline - 1 change saved on device');
    expect(await store.getPending(10)).toHaveLength(1);
    expect((await store.getStock('BC-JKT-001'))?.onHand).toBe(12); // optimistic local update
    expect(api.pushInventory).not.toHaveBeenCalled();
  });

  it('syncs queued counts automatically when connectivity returns', async () => {
    const { store, api, net } = await renderScreen(false);
    await enterCount('BC-JKT-001', '4');
    await screen.findByText('BC-JKT-001: 4');

    await act(async () => net.set(true));

    await waitFor(() => expect(api.pushInventory).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByText('Everything has synced.')).toBeTruthy());
    expect(await store.getPending(10)).toHaveLength(0);
  });

  it('validates input', async () => {
    await renderScreen(false);

    await enterCount('BC-JKT-001', '-3');

    expect(await screen.findByText('Count must be a whole number, 0 or more')).toBeTruthy();
  });
});
