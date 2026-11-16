import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';

import { api } from '@/core/api/http';
import { productSummarySchema } from '@/core/api/schemas';
import { useSession } from '@/core/auth/session';
import { keys } from '@/core/query';

export function useFavoriteIds() {
  const { user } = useSession();
  return useQuery({ queryKey: keys.favorites, queryFn: ({ signal }) => api('/me/favorites', { schema: z.array(z.string()), signal }), enabled: !!user, staleTime: 60_000 });
}

/** One batched request for all saved products (no N+1 from the list). */
export const useFavoriteProducts = (ids: string[]) =>
  useQuery({
    queryKey: ['me', 'favorites', 'products', ids],
    queryFn: ({ signal }) => api('/catalog/products/batch', { method: 'POST', body: { ids }, schema: z.array(productSummarySchema), auth: false, signal }),
    enabled: ids.length > 0,
    placeholderData: (prev) => prev,
  });

/**
 * Optimistic heart: the UI flips instantly, the request runs in the background, and the previous
 * state is restored if it fails. PUT/DELETE are idempotent server-side, so retries are safe.
 */
export function useToggleFavorite() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ productId, saved }: { productId: string; saved: boolean }) =>
      api(`/me/favorites/${productId}`, { method: saved ? 'DELETE' : 'PUT' }),
    onMutate: async ({ productId, saved }) => {
      await client.cancelQueries({ queryKey: keys.favorites });
      const previous = client.getQueryData<string[]>(keys.favorites);
      client.setQueryData<string[]>(keys.favorites, (ids = []) => (saved ? ids.filter((i) => i !== productId) : [productId, ...ids]));
      return { previous };
    },
    onError: (_e, _v, ctx) => client.setQueryData(keys.favorites, ctx?.previous),
    onSettled: () => client.invalidateQueries({ queryKey: keys.favorites }),
  });
}
