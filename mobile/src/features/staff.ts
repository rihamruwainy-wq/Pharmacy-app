import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';

import { api } from '@/core/api/http';
import {
  assistantResponseSchema, copilotResponseSchema, dashboardSchema, inventoryRowSchema, orderSchema, pagedSchema, productSchema, verificationItemSchema,
} from '@/core/api/schemas';
import { keys } from '@/core/query';

// ------------------------------------------------------------------ Pharmacist

/** The verification queue refreshes itself while the screen is open (new prescriptions arrive continuously). */
export const useQueue = () =>
  useQuery({ queryKey: keys.queue, queryFn: ({ signal }) => api('/pharmacist/queue', { schema: z.array(verificationItemSchema), signal }), refetchInterval: 30_000, staleTime: 10_000 });

export const useActiveOrders = () =>
  useQuery({ queryKey: keys.active, queryFn: ({ signal }) => api('/pharmacist/orders/active', { schema: z.array(orderSchema), signal }), refetchInterval: 30_000, staleTime: 10_000 });

export const useVerification = (id: string) =>
  useQuery({ queryKey: keys.verification(id), queryFn: ({ signal }) => api(`/pharmacist/orders/${id}`, { schema: verificationItemSchema, signal }) });

function useStaffMutation<T>(fn: (input: T) => Promise<unknown>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ['staff'] });
      client.invalidateQueries({ queryKey: ['admin', 'dashboard'] });
    },
  });
}

export const useVerify = (id: string) =>
  useStaffMutation((input: { approve: boolean; reason?: string }) => api(`/pharmacist/orders/${id}/verify`, { method: 'POST', body: input, schema: orderSchema }));

export const useAdvance = () =>
  useStaffMutation(({ id, to }: { id: string; to: string }) => api(`/pharmacist/orders/${id}/advance`, { method: 'POST', body: { to }, schema: orderSchema }));

/** AI calls are slower than CRUD: give them a longer timeout instead of failing at the default 10 s. */
export const useCopilot = () =>
  useMutation({
    mutationFn: (input: { question: string; orderId?: string }) =>
      api('/pharmacist/copilot', { method: 'POST', body: input, schema: copilotResponseSchema, timeoutMs: 120_000 }),
  });

// ------------------------------------------------------------------ Admin

export const useDashboard = () =>
  useQuery({ queryKey: keys.dashboard, queryFn: ({ signal }) => api('/admin/dashboard', { schema: dashboardSchema, signal }), staleTime: 30_000, refetchInterval: 60_000 });

export const useInventory = (filters: { lowOnly: boolean; q?: string }) =>
  useQuery({
    queryKey: keys.inventory(filters),
    queryFn: ({ signal }) =>
      api(`/admin/inventory?lowOnly=${filters.lowOnly}&pageSize=100${filters.q ? `&q=${encodeURIComponent(filters.q)}` : ''}`, { schema: pagedSchema(inventoryRowSchema), signal }),
    placeholderData: (prev) => prev,
  });

export function useRestock() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ branchId, productId, quantity }: { branchId: string; productId: string; quantity: number }) =>
      api(`/admin/inventory/${branchId}/${productId}/restock`, { method: 'POST', body: { quantity }, schema: inventoryRowSchema }),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ['admin'] });
      client.invalidateQueries({ queryKey: ['stock'] });
    },
  });
}

export function useReprice() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ productId, priceMinor }: { productId: string; priceMinor: number }) =>
      api(`/admin/products/${productId}/price`, { method: 'PUT', body: { priceMinor }, schema: productSchema }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['catalog'] }),
  });
}

export const useAssistant = () =>
  useMutation({
    mutationFn: (messages: { role: 'user' | 'assistant'; content: string }[]) =>
      api('/admin/assistant', { method: 'POST', body: { messages }, schema: assistantResponseSchema, timeoutMs: 120_000 }),
  });
