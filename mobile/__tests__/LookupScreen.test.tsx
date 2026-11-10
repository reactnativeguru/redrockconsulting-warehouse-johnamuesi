import { fireEvent, render, screen } from '@testing-library/react-native';
import React from 'react';
import type { Product } from '../src/api/types';
import { LookupScreen } from '../src/screens/LookupScreen';
import { buildServices, fakeApi } from './testUtils';

const jacket: Product = {
  sku: 'BC-JKT-001', name: 'Ridgeline Waterproof Jacket', description: '', category: 'Jackets',
  price: 89.99, barcode: '5060000000011', colours: ['Navy'], sizes: ['M'], attributes: {},
};

describe('LookupScreen', () => {
  it('shows product and local stock after a barcode scan', async () => {
    const api = fakeApi({ getProduct: jest.fn().mockResolvedValue(jacket) });
    const { store, wrapper: Wrapper } = buildServices({ api, online: true });
    await store.upsertStock([{ sku: 'BC-JKT-001', onHand: 8, reserved: 2, available: 6, reorderThreshold: 3, isLow: false, lastCountedAt: null, version: '5' }]);
    await render(<Wrapper><LookupScreen /></Wrapper>);

    // A hardware scanner "types" the barcode and presses Enter.
    await fireEvent.changeText(screen.getByTestId('lookup-input'), '5060000000011');
    await fireEvent(screen.getByTestId('lookup-input'), 'submitEditing');

    expect(await screen.findByText('Ridgeline Waterproof Jacket')).toBeTruthy();
    expect(screen.getByText('£89.99')).toBeTruthy();
    expect(screen.getByText('6')).toBeTruthy(); // available
    expect(api.getProduct).toHaveBeenCalledWith('5060000000011');
  });

  it('falls back to the on-device product cache when offline', async () => {
    const api = fakeApi(); // getProduct rejects
    const { store, wrapper: Wrapper } = buildServices({ api });
    await store.cacheProduct(jacket);
    await render(<Wrapper><LookupScreen /></Wrapper>);

    await fireEvent.changeText(screen.getByTestId('lookup-input'), 'bc-jkt-001');
    await fireEvent(screen.getByTestId('lookup-input'), 'submitEditing');

    expect(await screen.findByText('Offline - showing saved product details')).toBeTruthy();
    expect(screen.getByText('Ridgeline Waterproof Jacket')).toBeTruthy();
  });
});
