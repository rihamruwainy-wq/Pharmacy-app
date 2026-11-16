import AsyncStorage from '@react-native-async-storage/async-storage';
import { createAsyncStoragePersister } from '@tanstack/query-async-storage-persister';
import { focusManager, QueryClient, type Query } from '@tanstack/react-query';
import { AppState, Platform } from 'react-native';

import { ApiError } from './api/http';

/**
 * Query client tuned for a mobile pharmacy app:
 * - offlineFirst: cached data renders instantly, even with no signal; refetches happen in the background.
 * - Retries only transient failures (network/5xx/429) with capped exponential backoff; 4xx never retry.
 * - Per-resource freshness is set on each query (catalogue minutes, stock seconds, orders on demand).
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      networkMode: 'offlineFirst',
      staleTime: 30_000,
      gcTime: 24 * 60 * 60 * 1000,
      retry: (failureCount, error) => (error instanceof ApiError ? error.isRetryable : true) && failureCount < 2,
      retryDelay: (attempt, error) =>
        error instanceof ApiError && error.retryAfterSeconds ? error.retryAfterSeconds * 1000 : Math.min(10_000, 500 * 2 ** attempt),
      refetchOnReconnect: true,
    },
    mutations: { networkMode: 'online', retry: 0 },
  },
});

/** Refetch stale queries when the app returns to the foreground (native has no window focus events). */
if (Platform.OS !== 'web') {
  AppState.addEventListener('change', (state) => focusManager.setFocused(state === 'active'));
}

/**
 * Disk persistence for PUBLIC catalogue data only (queries with meta.persist). Orders, prescriptions,
 * allergies and staff data are health/PII data: they stay in memory and are never written to AsyncStorage.
 */
export const persister = createAsyncStoragePersister({ storage: AsyncStorage, key: 'medora.query-cache', throttleTime: 2000 });

export const persistOptions = {
  persister,
  maxAge: 24 * 60 * 60 * 1000,
  buster: 'catalog-v1', // bump when cached response shapes change
  dehydrateOptions: {
    shouldDehydrateQuery: (q: Query) => q.state.status === 'success' && q.meta?.persist === true,
  },
};

/** Query-key factory: one place defines keys, so invalidation can never miss a typo'd key. */
export const keys = {
  categories: ['catalog', 'categories'] as const,
  branches: ['catalog', 'branches'] as const,
  products: (filters: object) => ['catalog', 'products', filters] as const,
  product: (id: string) => ['catalog', 'product', id] as const,
  stock: (branchId: string, ids: string[]) => ['stock', branchId, [...ids].sort().join(',')] as const,
  me: ['me'] as const,
  favorites: ['me', 'favorites'] as const,
  orders: ['me', 'orders'] as const,
  order: (id: string) => ['me', 'order', id] as const,
  prescriptions: ['me', 'prescriptions'] as const,
  appointments: ['me', 'appointments'] as const,
  careServices: ['care', 'services'] as const,
  slots: (serviceId: string) => ['care', 'slots', serviceId] as const,
  queue: ['staff', 'queue'] as const,
  active: ['staff', 'active'] as const,
  verification: (id: string) => ['staff', 'order', id] as const,
  dashboard: ['admin', 'dashboard'] as const,
  inventory: (filters: object) => ['admin', 'inventory', filters] as const,
};
