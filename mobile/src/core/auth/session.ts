import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';
import { useSyncExternalStore } from 'react';

import type { AuthResponse, User } from '../api/schemas';

/**
 * Session store (framework-free, so the HTTP client can use it outside React).
 * Native: tokens live in the OS keychain/keystore via SecureStore (encrypted at rest, never in AsyncStorage).
 * Web preview: localStorage (acceptable for the dev preview; a production web app would use the BFF
 * cookie pattern from the Airbnb clone instead).
 */
export interface Session {
  user: User;
  accessToken: string;
  accessTokenExpiresAt: number;
  refreshToken: string;
}

const KEY = 'medora.session.v1';
let current: Session | null = null;
let loaded = false;
const listeners = new Set<() => void>();

const storage = {
  get: async () => (Platform.OS === 'web' ? globalThis.localStorage?.getItem(KEY) ?? null : SecureStore.getItemAsync(KEY)),
  set: async (v: string) => (Platform.OS === 'web' ? globalThis.localStorage?.setItem(KEY, v) : SecureStore.setItemAsync(KEY, v)),
  del: async () => (Platform.OS === 'web' ? globalThis.localStorage?.removeItem(KEY) : SecureStore.deleteItemAsync(KEY)),
};

function emit() {
  listeners.forEach((l) => l());
}

export async function loadSession(): Promise<Session | null> {
  if (loaded) return current;
  try {
    const raw = await storage.get();
    current = raw ? (JSON.parse(raw) as Session) : null;
  } catch {
    current = null; // corrupted entry: treat as signed out
  }
  loaded = true;
  emit();
  return current;
}

export async function saveSession(auth: AuthResponse): Promise<Session> {
  current = {
    user: auth.user,
    accessToken: auth.accessToken,
    accessTokenExpiresAt: Date.parse(auth.accessTokenExpiresAt),
    refreshToken: auth.refreshToken,
  };
  await storage.set(JSON.stringify(current));
  emit();
  return current;
}

export async function updateUser(user: User) {
  if (!current) return;
  current = { ...current, user };
  await storage.set(JSON.stringify(current));
  emit();
}

export async function clearSession() {
  current = null;
  await storage.del();
  emit();
}

export const getSession = () => current;
export const isSessionLoaded = () => loaded;

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** React binding: re-renders on sign-in/out and token rotation. */
export function useSession() {
  const session = useSyncExternalStore(subscribe, () => current, () => current);
  const ready = useSyncExternalStore(subscribe, () => loaded, () => loaded);
  const roles = session?.user.roles ?? [];
  return {
    session,
    ready,
    user: session?.user ?? null,
    isStaff: roles.includes('pharmacist') || roles.includes('admin'),
    isAdmin: roles.includes('admin'),
  };
}
