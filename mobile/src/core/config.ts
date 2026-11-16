import { Platform } from 'react-native';

/**
 * API base URL. Set EXPO_PUBLIC_API_URL for devices/staging (e.g. http://192.168.1.6:5280 for a phone on
 * the same Wi-Fi). Defaults: Android emulator reaches the host via 10.0.2.2; web/iOS simulator via localhost.
 */
export const API_URL =
  process.env.EXPO_PUBLIC_API_URL ?? (Platform.OS === 'android' ? 'http://10.0.2.2:5280' : 'http://localhost:5280');

/** Test mode: the API's dev payment gateway honours Stripe's published test payment-method ids. */
export const TEST_PAYMENT_METHODS = [
  { id: 'pm_card_visa', label: 'Visa •••• 4242', note: 'Succeeds' },
  { id: 'pm_card_mastercard', label: 'Mastercard •••• 4444', note: 'Succeeds' },
  { id: 'pm_card_chargeDeclined', label: 'Visa •••• 0002', note: 'Declined' },
  { id: 'pm_card_chargeDeclinedInsufficientFunds', label: 'Visa •••• 9995', note: 'Insufficient funds' },
] as const;
