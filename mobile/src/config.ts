/**
 * Runtime configuration. EXPO_PUBLIC_* variables are inlined at build time by Expo.
 *
 * API_URL notes:
 *  - iOS simulator / web:      http://localhost:5050
 *  - Android emulator:         http://10.0.2.2:5050   (emulator's alias for your machine)
 *  - Physical device:          http://<your-LAN-IP>:5050
 *  - Azure:                    https://<gateway>.azurecontainerapps.io
 */
export const config = {
  apiUrl: process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5050',
  storeId: process.env.EXPO_PUBLIC_STORE_ID ?? 'STORE-001',
  deviceId: process.env.EXPO_PUBLIC_DEVICE_ID ?? 'HANDHELD-DEV-01',
  syncIntervalMs: 30_000,
};
