import React, { useState } from 'react';
import { FlatList, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { useSync } from '../sync/SyncProvider';
import { colours } from '../theme';

/**
 * Record physical stock counts. Works identically online and offline: the count is
 * written to the on-device outbox first, then synced. The colleague never waits
 * on the network (stockrooms are notorious for poor Wi-Fi).
 */
export function StockCountScreen() {
  const { record, mutations } = useSync();
  const [sku, setSku] = useState('');
  const [quantity, setQuantity] = useState('');
  const [error, setError] = useState<string | null>(null);

  const counts = mutations.filter((m) => m.kind === 'StockCount' || m.kind === 'StockAdjustment');

  async function save() {
    const qty = Number(quantity);
    if (!sku.trim()) return setError('Scan or enter a SKU');
    if (!Number.isInteger(qty) || qty < 0) return setError('Count must be a whole number, 0 or more');
    setError(null);
    await record({ kind: 'StockCount', sku: sku.trim().toUpperCase(), quantity: qty });
    setSku('');
    setQuantity('');
  }

  return (
    <View style={styles.container}>
      <TextInput
        style={styles.input}
        placeholder="SKU (scan)"
        value={sku}
        onChangeText={setSku}
        autoCapitalize="characters"
        testID="count-sku"
      />
      <TextInput
        style={styles.input}
        placeholder="Counted quantity"
        value={quantity}
        onChangeText={setQuantity}
        keyboardType="number-pad"
        testID="count-quantity"
        onSubmitEditing={save}
      />
      {error && <Text style={styles.error}>{error}</Text>}
      <Pressable accessibilityRole="button" style={styles.button} onPress={save}>
        <Text style={styles.buttonText}>Save count</Text>
      </Pressable>

      <Text style={styles.heading}>Unsynced counts</Text>
      <FlatList
        data={counts}
        keyExtractor={(m) => m.id}
        ListEmptyComponent={<Text style={styles.muted}>Everything has synced.</Text>}
        renderItem={({ item }) => (
          <View style={styles.row} testID="pending-count">
            <Text style={styles.rowTitle}>{item.sku}: {item.quantity}</Text>
            <Text style={item.status === 'rejected' ? styles.error : styles.muted}>
              {item.status === 'rejected' ? `Rejected: ${item.lastError}` : 'Waiting to sync'}
            </Text>
          </View>
        )}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, gap: 10 },
  input: { backgroundColor: colours.white, borderColor: colours.border, borderWidth: 1, borderRadius: 8, padding: 14, fontSize: 18 },
  button: { backgroundColor: colours.primary, padding: 14, borderRadius: 8, alignItems: 'center' },
  buttonText: { color: colours.white, fontWeight: '700', fontSize: 16 },
  heading: { marginTop: 12, fontWeight: '700', fontSize: 16 },
  row: { backgroundColor: colours.white, padding: 12, borderRadius: 8, marginBottom: 6 },
  rowTitle: { fontWeight: '600' },
  muted: { color: colours.muted },
  error: { color: colours.danger },
});
