import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';

import { api } from '@/core/api/http';
import { authResponseSchema, prescriptionSchema, userSchema } from '@/core/api/schemas';
import { clearSession, getSession, saveSession, updateUser, useSession } from '@/core/auth/session';
import { keys, queryClient } from '@/core/query';

export const lookupEmail = (email: string) => api('/auth/lookup', { method: 'POST', body: { email }, schema: z.object({ exists: z.boolean() }), auth: false });

export async function login(email: string, password: string) {
  const auth = await api('/auth/login', { method: 'POST', body: { email, password }, schema: authResponseSchema, auth: false });
  await saveSession(auth);
  queryClient.removeQueries({ queryKey: ['me'] }); // never show the previous user's cached data
  return auth.user;
}

export async function register(input: { firstName: string; lastName: string; email: string; password: string; birthDate: string }) {
  const auth = await api('/auth/register', { method: 'POST', body: input, schema: authResponseSchema, auth: false });
  await saveSession(auth);
  return auth.user;
}

export async function logout() {
  const session = getSession();
  // Revoke server-side first (best effort), then forget locally and drop every per-user cache entry.
  if (session) await api('/auth/logout', { method: 'POST', body: { refreshToken: session.refreshToken }, auth: false }).catch(() => undefined);
  await clearSession();
  queryClient.removeQueries({ queryKey: ['me'] });
  queryClient.removeQueries({ queryKey: ['staff'] });
  queryClient.removeQueries({ queryKey: ['admin'] });
}

export function useUpdateProfile() {
  return useMutation({
    mutationFn: (input: { firstName: string; lastName: string; allergies?: string }) => api('/me', { method: 'PUT', body: input, schema: userSchema }),
    onSuccess: (user) => updateUser(user),
  });
}

export function usePrescriptions() {
  const { user } = useSession();
  return useQuery({ queryKey: keys.prescriptions, queryFn: ({ signal }) => api('/me/prescriptions', { schema: z.array(prescriptionSchema), signal }), enabled: !!user });
}

export function useSubmitPrescription() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: { patientName: string; prescriberName: string; prescriberLicense?: string; imageReference: string; notes?: string; issuedOn: string }) =>
      api('/me/prescriptions', { method: 'POST', body: input, schema: prescriptionSchema }),
    onSuccess: (rx) => client.setQueryData(keys.prescriptions, (old: z.infer<typeof prescriptionSchema>[] | undefined) => [rx, ...(old ?? [])]),
  });
}
