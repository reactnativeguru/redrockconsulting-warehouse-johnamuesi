import React, { useState } from 'react';
import { ActivityIndicator, StyleSheet, Text, TextInput, View } from 'react-native';
import type { Product, StockDto } from '../api/types';
import { useSync } from '../sync/SyncProvider';
import { colours } from '../theme';

/**
 * Scan (or type) a barcode/SKU -> product details + this store's stock.
 *
 * Handheld scanners (e.g. Zebra devices with DataWedge) usually act as a keyboard:
 * the scan "types" the barcode and presses Enter. So a focused TextInput with
 * onSubmitEditing is all we need - no camera code. (expo-camera's barcode
 * scanning is the option for phones without a hardware scanner.)
 */
export function LookupScreen() {
  const { services } = useSync();
  const [query, setQuery] = useState('');
  const [loading, setLoading] = useState(false);
  const [product, setProduct] = useState<Product | null>(null);
  const [stock, setStock] = useState<StockDto | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  async function lookup() {
    const value = query.trim();
    if (!value) return;
    setLoading(true);
    setMessage(null);
    try {
      let p: Product | null;
      try {
        p = await services.api.getProduct(value);
        await services.store.cacheProduct(p);
      } catch {
        // Offline (or not found online): fall back to the on-device cache.
        p = await services.store.getCachedProduct(value);
        if (p) setMessage('Offline - showing saved product details');
      }
      setProduct(p);
      setStock(p ? await services.store.getStock(p.sku) : null);
      if (!p) setMessage(`No product found for "${value}"`);
    } finally {
      setLoading(false);
      setQuery('');
    }
  }

  return (
    <View style={styles.container}>
      <TextInput
        style={styles.input}
        placeholder="Scan or type barcode / SKU"
        value={query}
        onChangeText={setQuery}
        onSubmitEditing={lookup}
        autoFocus
        autoCapitalize="characters"
        returnKeyType="search"
        testID="lookup-input"
      />
      {loading && <ActivityIndicator color={colours.primary} />}
      {message && <Text style={styles.message}>{message}</Text>}
      {product && (
        <View style={styles.card} testID="product-card">
          <Text style={styles.title}>{product.name}</Text>
          <Text style={styles.muted}>{product.sku} · {product.barcode}</Text>
          <Text style={styles.price}>£{product.price.toFixed(2)}</Text>
          {product.colours.length > 0 && <Text>Colours: {product.colours.join(', ')}</Text>}
          {product.sizes.length > 0 && <Text>Sizes: {product.sizes.join(', ')}</Text>}
          <View style={styles.stockRow}>
            {stock ? (
              <>
                <Stat label="On hand" value={stock.onHand} />
                <Stat label="Reserved" value={stock.reserved} />
                <Stat label="Available" value={stock.available} highlight={stock.isLow} />
              </>
            ) : (
              <Text style={styles.muted}>No stock record for this store yet - sync to refresh.</Text>
            )}
          </View>
        </View>
      )}
    </View>
  );
}

function Stat({ label, value, highlight }: { label: string; value: number; highlight?: boolean }) {
  return (
    <View style={styles.stat}>
      <Text style={[styles.statValue, highlight && { color: colours.danger }]}>{value}</Text>
      <Text style={styles.muted}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, gap: 12 },
  input: { backgroundColor: colours.white, borderColor: colours.border, borderWidth: 1, borderRadius: 8, padding: 14, fontSize: 18 },
  message: { color: colours.warning },
  card: { backgroundColor: colours.white, borderRadius: 8, padding: 16, gap: 4 },
  title: { fontSize: 20, fontWeight: '700', color: colours.text },
  muted: { color: colours.muted },
  price: { fontSize: 18, fontWeight: '600', color: colours.primary, marginVertical: 4 },
  stockRow: { flexDirection: 'row', justifyContent: 'space-around', marginTop: 12 },
  stat: { alignItems: 'center' },
  statValue: { fontSize: 24, fontWeight: '700' },
});
