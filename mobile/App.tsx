import { StatusBar } from 'expo-status-bar';
import React, { useEffect, useState } from 'react';
import { ActivityIndicator, Platform, Pressable, StatusBar as RNStatusBar, StyleSheet, Text, View } from 'react-native';
import { SyncStatusBar } from './src/components/SyncStatusBar';
import { LookupScreen } from './src/screens/LookupScreen';
import { PickListScreen } from './src/screens/PickListScreen';
import { StockCountScreen } from './src/screens/StockCountScreen';
import { createAppServices } from './src/services';
import { SyncProvider, type AppServices } from './src/sync/SyncProvider';
import { colours } from './src/theme';

type Tab = 'lookup' | 'count' | 'pick';
const TABS: { key: Tab; label: string }[] = [
  { key: 'lookup', label: 'Lookup' },
  { key: 'count', label: 'Stock count' },
  { key: 'pick', label: 'Click & collect' },
];

export default function App() {
  const [services, setServices] = useState<AppServices | null>(null);

  useEffect(() => {
    void createAppServices().then(setServices);
  }, []);

  if (!services) {
    return (
      <View style={styles.center}>
        <ActivityIndicator color={colours.primary} />
      </View>
    );
  }

  return (
    <SyncProvider services={services}>
      <Shell storeId={services.storeId} />
    </SyncProvider>
  );
}

/**
 * Deliberately simple tab state instead of a navigation library - a handheld app
 * with three task-focused screens doesn't need deep linking or stacks yet.
 * (Swap in React Navigation when it does.)
 */
export function Shell({ storeId }: { storeId: string }) {
  const [tab, setTab] = useState<Tab>('lookup');
  return (
    <View style={styles.root}>
      <StatusBar style="light" />
      <View style={styles.header}>
        <Text style={styles.headerTitle}>Basecamp Store</Text>
        <Text style={styles.headerSub}>{storeId}</Text>
      </View>
      <SyncStatusBar />
      <View style={styles.body}>
        {tab === 'lookup' && <LookupScreen />}
        {tab === 'count' && <StockCountScreen />}
        {tab === 'pick' && <PickListScreen />}
      </View>
      <View style={styles.tabBar}>
        {TABS.map((t) => (
          <Pressable
            key={t.key}
            accessibilityRole="tab"
            accessibilityState={{ selected: tab === t.key }}
            onPress={() => setTab(t.key)}
            style={[styles.tab, tab === t.key && styles.tabActive]}
          >
            <Text style={[styles.tabText, tab === t.key && styles.tabTextActive]}>{t.label}</Text>
          </Pressable>
        ))}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: colours.background },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center' },
  header: {
    backgroundColor: colours.primary,
    paddingTop: Platform.OS === 'android' ? (RNStatusBar.currentHeight ?? 24) + 8 : 54,
    paddingBottom: 12,
    paddingHorizontal: 16,
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-end',
  },
  headerTitle: { color: colours.white, fontSize: 20, fontWeight: '700' },
  headerSub: { color: colours.primaryLight },
  body: { flex: 1 },
  tabBar: { flexDirection: 'row', borderTopWidth: 1, borderColor: colours.border, backgroundColor: colours.white, paddingBottom: 20 },
  tab: { flex: 1, padding: 14, alignItems: 'center' },
  tabActive: { borderTopWidth: 3, borderColor: colours.primary },
  tabText: { color: colours.muted, fontWeight: '600' },
  tabTextActive: { color: colours.primary },
});
