import React from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useSync } from '../sync/SyncProvider';
import { colours } from '../theme';

/** Always-visible banner: colleagues must know whether their work has reached the server. */
export function SyncStatusBar() {
  const { online, syncing, pendingCount, rejectedCount, lastSyncAt, syncNow } = useSync();

  const label = !online
    ? `Offline - ${pendingCount} change${pendingCount === 1 ? '' : 's'} saved on device`
    : syncing
      ? 'Syncing...'
      : pendingCount > 0
        ? `${pendingCount} waiting to sync`
        : `Up to date${lastSyncAt ? ` (${new Date(lastSyncAt).toLocaleTimeString()})` : ''}`;

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel="Sync now"
      onPress={() => void syncNow()}
      style={[styles.bar, { backgroundColor: online ? colours.primary : colours.warning }]}
    >
      <Text style={styles.text} testID="sync-status">{label}</Text>
      {rejectedCount > 0 && (
        <View style={styles.badge}>
          <Text style={styles.badgeText}>{rejectedCount} need attention</Text>
        </View>
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  bar: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingHorizontal: 16, paddingVertical: 8 },
  text: { color: 'white', fontWeight: '600' },
  badge: { backgroundColor: colours.danger, borderRadius: 10, paddingHorizontal: 8, paddingVertical: 2 },
  badgeText: { color: 'white', fontSize: 12, fontWeight: '700' },
});
