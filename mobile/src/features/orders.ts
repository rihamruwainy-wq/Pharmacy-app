import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';

import { api, newIdempotencyKey } from '@/core/api/http';
import { appointmentSchema, careServiceSchema, orderSchema, quoteSchema, slotSchema, type Order } from '@/core/api/schemas';
import { useSession } from '@/core/auth/session';
import { keys } from '@/core/query';

type Lines = { productId: string; quantity: number }[];

/** Server-side quote: the price the customer will actually pay (client prices are only a preview). */
export const useQuote = (branchId: string, fulfilment: string, lines: Lines, enabled: boolean) =>
  useQuery({
    queryKey: ['quote', branchId, fulfilment, lines],
    queryFn: ({ signal }) => api('/orders/quote', { method: 'POST', body: { branchId, fulfilment, lines }, schema: quoteSchema, signal }),
    enabled: enabled && lines.length > 0,
    staleTime: 30_000,
    placeholderData: (prev) => prev, // keep showing the last total while a quantity change re-quotes
  });

/**
 * Place order. The idempotency key is created ONCE per checkout intent (by the caller) and reused if
 * the request is retried (timeout, flaky network, double tap): the server returns the same order.
 */
export function usePlaceOrder() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ key, ...body }: { key: string; branchId: string; fulfilment: string; deliveryAddress?: string; lines: Lines; prescriptionId?: string }) =>
      api('/orders', { method: 'POST', body, schema: orderSchema, idempotencyKey: key }),
    onSuccess: (order) => {
      client.setQueryData(keys.order(order.id), order);
      client.invalidateQueries({ queryKey: keys.orders });
      client.invalidateQueries({ queryKey: ['stock'] });
    },
  });
}

export function usePay(orderId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (paymentMethodId: string) => api(`/orders/${orderId}/pay`, { method: 'POST', body: { paymentMethodId }, schema: orderSchema }),
    onSuccess: (order) => {
      client.setQueryData(keys.order(order.id), order);
      client.invalidateQueries({ queryKey: keys.orders });
    },
  });
}

export function useCancelOrder(orderId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => api(`/orders/${orderId}/cancel`, { method: 'POST', schema: orderSchema }),
    onSuccess: (order) => {
      client.setQueryData(keys.order(order.id), order);
      client.invalidateQueries({ queryKey: keys.orders });
      client.invalidateQueries({ queryKey: ['stock'] });
    },
  });
}

const ACTIVE: Order['status'][] = ['pending_payment', 'awaiting_verification', 'preparing', 'ready_for_pickup', 'out_for_delivery'];

/** Order detail polls while the order is in motion (status changes come from the pharmacy), and stops when it settles. */
export const useOrder = (id: string) =>
  useQuery({
    queryKey: keys.order(id),
    queryFn: ({ signal }) => api(`/orders/${id}`, { schema: orderSchema, signal }),
    refetchInterval: (q) => (q.state.data && ACTIVE.includes(q.state.data.status) ? 20_000 : false),
  });

export function useMyOrders() {
  const { user } = useSession();
  return useQuery({ queryKey: keys.orders, queryFn: ({ signal }) => api('/me/orders', { schema: z.array(orderSchema), signal }), enabled: !!user });
}

// ------------------------------------------------------------------ Care services (appointments)

export const useCareServices = () =>
  useQuery({ queryKey: keys.careServices, queryFn: ({ signal }) => api('/care/services', { schema: z.array(careServiceSchema), auth: false, signal }), staleTime: 3600_000, meta: { persist: true } });

export const useSlots = (serviceId: string) =>
  useQuery({ queryKey: keys.slots(serviceId), queryFn: ({ signal }) => api(`/care/services/${serviceId}/slots?days=7`, { schema: z.array(slotSchema), auth: false, signal }), staleTime: 20_000 });

export function useMyAppointments() {
  const { user } = useSession();
  return useQuery({ queryKey: keys.appointments, queryFn: ({ signal }) => api('/me/appointments', { schema: z.array(appointmentSchema), signal }), enabled: !!user });
}

export function useBook(serviceId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: { slotId: string; notes?: string }) => api('/care/appointments', { method: 'POST', body: input, schema: appointmentSchema }),
    onSettled: () => {
      client.invalidateQueries({ queryKey: keys.slots(serviceId) }); // spots changed either way (booked, or someone else got it)
      client.invalidateQueries({ queryKey: keys.appointments });
    },
  });
}

export function useCancelAppointment() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api(`/care/appointments/${id}/cancel`, { method: 'POST', schema: appointmentSchema }),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.appointments }),
  });
}

export { newIdempotencyKey };
