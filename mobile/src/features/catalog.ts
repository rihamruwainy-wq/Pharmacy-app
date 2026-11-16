import AsyncStorage from '@react-native-async-storage/async-storage';
import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useState } from 'react';
import { z } from 'zod';

import { api } from '@/core/api/http';
import { branchSchema, categorySchema, productSchema, searchResultSchema, stockSchema, suggestSchema } from '@/core/api/schemas';
import { keys } from '@/core/query';

export type SortOption = 'relevance' | 'price_asc' | 'price_desc' | 'rating';

export interface ProductFilters {
  q?: string;
  category?: string;
  sort?: SortOption;
  /** true = prescription only, false = over the counter, undefined = both. */
  rx?: boolean;
  maxPriceMinor?: number;
  /** Only products available now at this branch. */
  inStockAt?: string;
}

const PAGE_SIZE = 20;

function qs(params: Record<string, string | number | boolean | undefined>) {
  const s = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => v !== undefined && v !== '' && s.set(k, String(v)));
  return s.toString();
}

/** Catalogue data changes rarely: long freshness + persisted to disk => instant cold start and offline browsing. */
export const useCategories = () =>
  useQuery({ queryKey: keys.categories, queryFn: ({ signal }) => api('/catalog/categories', { schema: z.array(categorySchema), auth: false, signal }), staleTime: 24 * 3600_000, meta: { persist: true } });

export const useBranches = () =>
  useQuery({ queryKey: keys.branches, queryFn: ({ signal }) => api('/catalog/branches', { schema: z.array(branchSchema), auth: false, signal }), staleTime: 3600_000, meta: { persist: true } });

/**
 * Infinite, filterable search. The server understands brands, symptoms, Arabic and typos, and reports
 * `didYouMean` / `matchedCategories` on the first page. `signal` cancels superseded searches.
 */
export const useProducts = (filters: ProductFilters) =>
  useInfiniteQuery({
    queryKey: keys.products(filters),
    initialPageParam: 1,
    queryFn: ({ pageParam, signal }) =>
      api(`/catalog/products?${qs({
        q: filters.q, category: filters.category, sort: filters.sort, rx: filters.rx, maxPrice: filters.maxPriceMinor,
        inStock: filters.inStockAt ? true : undefined, branchId: filters.inStockAt, page: pageParam, pageSize: PAGE_SIZE,
      })}`, { schema: searchResultSchema, auth: false, signal }),
    getNextPageParam: (last) => (last.page * last.pageSize < last.total ? last.page + 1 : undefined),
    staleTime: filters.inStockAt ? 15_000 : 5 * 60_000,
    meta: { persist: !filters.inStockAt && !filters.q }, // persist browse pages, not every typed query
  });

/** Type-ahead suggestions (products + matching needs), cached briefly and cancelled while typing. */
export const useSuggestions = (q: string) =>
  useQuery({
    queryKey: ['catalog', 'suggest', q.trim().toLowerCase()],
    queryFn: ({ signal }) => api(`/catalog/suggest?${qs({ q })}`, { schema: suggestSchema, auth: false, signal }),
    enabled: q.trim().length >= 2,
    staleTime: 5 * 60_000,
    placeholderData: (prev) => prev,
  });

export const fetchProduct = (id: string, signal?: AbortSignal) => api(`/catalog/products/${encodeURIComponent(id)}`, { schema: productSchema, auth: false, signal });

export const useProduct = (id: string) =>
  useQuery({ queryKey: keys.product(id), queryFn: ({ signal }) => fetchProduct(id, signal), staleTime: 5 * 60_000, meta: { persist: true } });

/** Warm the detail cache on press-in so the next screen renders without a spinner. */
export function usePrefetchProduct() {
  const client = useQueryClient();
  return useCallback((id: string) => client.prefetchQuery({ queryKey: keys.product(id), queryFn: ({ signal }) => fetchProduct(id, signal), staleTime: 5 * 60_000 }), [client]);
}

/** Live stock moves with every order: short freshness, never persisted. */
export const useStock = (branchId: string | undefined, productIds: string[]) =>
  useQuery({
    queryKey: keys.stock(branchId ?? '-', productIds),
    queryFn: ({ signal }) => api(`/catalog/branches/${branchId}/stock?ids=${productIds.join(',')}`, { schema: z.array(stockSchema), auth: false, signal }),
    enabled: !!branchId && productIds.length > 0,
    staleTime: 15_000,
  });

const RECENT_KEY = 'medora.recent-searches.v1';
const RECENT_MAX = 8;

/** Recent searches, kept on the device only (newest first, de-duplicated, capped). */
export function useRecentSearches() {
  const [recent, setRecent] = useState<string[]>([]);
  useEffect(() => {
    AsyncStorage.getItem(RECENT_KEY).then((raw) => raw && setRecent(JSON.parse(raw) as string[])).catch(() => undefined);
  }, []);
  const save = useCallback((next: string[]) => {
    setRecent(next);
    AsyncStorage.setItem(RECENT_KEY, JSON.stringify(next)).catch(() => undefined);
  }, []);
  return {
    recent,
    add: (q: string) => {
      const term = q.trim();
      if (term.length < 2) return;
      save([term, ...recent.filter((r) => r.toLowerCase() !== term.toLowerCase())].slice(0, RECENT_MAX));
    },
    remove: (q: string) => save(recent.filter((r) => r !== q)),
    clear: () => save([]),
  };
}

/** Everyday needs shown as one-tap chips. The query term is understood by the server in any language. */
export const NEEDS = [
  { key: 'need.headache', query: 'headache', icon: 'thermometer-outline' },
  { key: 'need.fever', query: 'fever', icon: 'flame-outline' },
  { key: 'need.allergy', query: 'allergy', icon: 'flower-outline' },
  { key: 'need.cough', query: 'cough', icon: 'cloudy-outline' },
  { key: 'need.heartburn', query: 'heartburn', icon: 'bonfire-outline' },
  { key: 'need.diarrhoea', query: 'diarrhoea', icon: 'water-outline' },
  { key: 'need.bloodPressure', query: 'blood pressure', icon: 'heart-outline' },
  { key: 'need.diabetes', query: 'diabetes', icon: 'pulse-outline' },
  { key: 'need.skin', query: 'itch', icon: 'hand-left-outline' },
  { key: 'need.baby', query: 'baby', icon: 'happy-outline' },
] as const;

/** Category ids -> icons (labels are translated on the device). */
export const CATEGORY_ICONS: Record<string, string> = {
  'pain-relief': 'thermometer-outline',
  'cold-flu': 'cloudy-outline',
  allergy: 'flower-outline',
  digestive: 'nutrition-outline',
  vitamins: 'leaf-outline',
  heart: 'heart-outline',
  diabetes: 'pulse-outline',
  antibiotics: 'medical-outline',
  'mental-health': 'happy-outline',
  respiratory: 'fitness-outline',
  skin: 'sparkles-outline',
  baby: 'balloon-outline',
};
