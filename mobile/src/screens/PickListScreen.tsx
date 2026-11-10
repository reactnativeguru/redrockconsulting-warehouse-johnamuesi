import React, { useCallback, useEffect, useState } from 'react';
import { FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import type { Order } from '../api/types';
import { useSync } from '../sync/SyncProvider';
import { colours } from '../theme';

/**
 * Click & collect pick list. Loading the list needs the network (orders arrive from
 * the website), but MARKING an order picked is queued like any other mutation, so
 * a colleague can keep picking in a dead spot and it syncs when signal returns.
 */
export function PickListScreen() {
  const { services, record, online, mutations } = useSync();
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const pickedIds = new Set(mutations.filter((m) => m.kind === 'OrderPicked').map((m) => m.orderId));

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setOrders(await services.api.getPickList(services.storeId));
      setError(null);
    } catch {
      setError('Could not load orders - check connection');
    } finally {
      setLoading(false);
    }
  }, [services]);

  useEffect(() => {
    if (online) void load();
  }, [online, load]);

  // Hide orders already picked on this device (optimistic), even before sync.
  const visible = orders.filter((o) => !pickedIds.has(o.id));

  return (
    <FlatList
      contentContainerStyle={styles.container}
      data={visible}
      keyExtractor={(o) => o.id}
      refreshControl={<RefreshControl refreshing={loading} onRefresh={load} />}
      ListHeaderComponent={error ? <Text style={styles.error}>{error}</Text> : undefined}
      ListEmptyComponent={!loading ? <Text style={styles.muted}>Nothing to pick right now.</Text> : undefined}
      renderItem={({ item }) => (
        <View style={styles.card}>
          <Text style={styles.title}>{item.customerName}</Text>
          <Text style={styles.muted}>#{item.id.slice(0, 8)} · {new Date(item.createdAt).toLocaleString()}</Text>
          {item.lines.map((l) => (
            <Text key={l.sku}>• {l.quantity} × {l.productName} ({l.sku})</Text>
          ))}
          <Pressable
            accessibilityRole="button"
            style={styles.button}
            onPress={() => void record({ kind: 'OrderPicked', orderId: item.id })}
          >
            <Text style={styles.buttonText}>Mark picked</Text>
          </Pressable>
        </View>
      )}
    />
  );
}

const styles = StyleSheet.create({
  container: { padding: 16, gap: 10 },
  card: { backgroundColor: colours.white, borderRadius: 8, padding: 16, gap: 4 },
  title: { fontSize: 18, fontWeight: '700' },
  muted: { color: colours.muted },
  error: { color: colours.danger, marginBottom: 8 },
  button: { backgroundColor: colours.primary, padding: 12, borderRadius: 8, alignItems: 'center', marginTop: 8 },
  buttonText: { color: colours.white, fontWeight: '700' },
});
