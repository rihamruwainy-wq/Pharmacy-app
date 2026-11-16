import { z } from 'zod';

/**
 * Runtime contracts for every API response. TypeScript types vanish at runtime; the network is an
 * untrusted boundary, so responses are parsed (contract drift fails loudly in one place).
 * Mirrors backend/src/Medora.Application/Contracts/Contracts.cs - unset optional fields are OMITTED.
 */
export const userSchema = z.object({
  id: z.string(),
  firstName: z.string(),
  lastName: z.string(),
  email: z.string(),
  roles: z.array(z.string()),
  branchId: z.string().optional(),
  allergies: z.string().optional(),
});
export type User = z.infer<typeof userSchema>;

export const authResponseSchema = z.object({
  user: userSchema,
  accessToken: z.string(),
  accessTokenExpiresAt: z.string(),
  refreshToken: z.string(),
  refreshTokenExpiresAt: z.string(),
});
export type AuthResponse = z.infer<typeof authResponseSchema>;

export const pagedSchema = <T extends z.ZodType>(item: T) =>
  z.object({ items: z.array(item), page: z.number(), pageSize: z.number(), total: z.number() });

export const categorySchema = z.object({ id: z.string(), label: z.string(), icon: z.string() });
export type Category = z.infer<typeof categorySchema>;

export const branchSchema = z.object({
  id: z.string(),
  name: z.string(),
  city: z.string(),
  address: z.string(),
  latitude: z.number(),
  longitude: z.number(),
  openingHours: z.string(),
  deliveryFeeMinor: z.number(),
  open24h: z.boolean(),
});
export type Branch = z.infer<typeof branchSchema>;

export const productSummarySchema = z.object({
  id: z.string(),
  name: z.string(),
  brand: z.string(),
  category: z.string(),
  form: z.string(),
  strength: z.string(),
  priceMinor: z.number(),
  currency: z.string(),
  requiresPrescription: z.boolean(),
  imageUrl: z.string(),
  rating: z.number(),
  reviewCount: z.number(),
});
export type ProductSummary = z.infer<typeof productSummarySchema>;

export const productSchema = productSummarySchema.extend({
  genericName: z.string(),
  maxQtyPerOrder: z.number(),
  description: z.string(),
  activeIngredients: z.array(z.string()),
});
export type Product = z.infer<typeof productSchema>;

/** Search page plus what the server understood (typo correction, symptom -> categories). */
export const searchResultSchema = z.object({
  items: z.array(productSummarySchema),
  page: z.number(),
  pageSize: z.number(),
  total: z.number(),
  didYouMean: z.string().optional(),
  matchedCategories: z.array(z.string()),
});
export type SearchResult = z.infer<typeof searchResultSchema>;

export const suggestSchema = z.object({
  products: z.array(z.object({ id: z.string(), name: z.string(), brand: z.string(), requiresPrescription: z.boolean() })),
  categories: z.array(z.string()),
  didYouMean: z.string().optional(),
});
export type Suggestions = z.infer<typeof suggestSchema>;

export const stockSchema =z.object({ productId: z.string(), status: z.enum(['in_stock', 'low', 'out']), available: z.number() });
export type Stock = z.infer<typeof stockSchema>;

export const orderStatusSchema = z.enum([
  'pending_payment', 'awaiting_verification', 'preparing', 'ready_for_pickup', 'out_for_delivery', 'completed', 'expired', 'cancelled', 'rejected',
]);
export type OrderStatus = z.infer<typeof orderStatusSchema>;

export const quoteSchema = z.object({
  currency: z.string(),
  subtotalMinor: z.number(),
  deliveryFeeMinor: z.number(),
  serviceFeeMinor: z.number(),
  rxHandlingFeeMinor: z.number(),
  totalMinor: z.number(),
  requiresPrescription: z.boolean(),
});
export type Quote = z.infer<typeof quoteSchema>;

export const orderSchema = z.object({
  id: z.string(),
  branchId: z.string(),
  fulfilment: z.enum(['pickup', 'delivery']),
  deliveryAddress: z.string().optional(),
  status: orderStatusSchema,
  lines: z.array(z.object({
    productId: z.string(), name: z.string(), unitPriceMinor: z.number(), quantity: z.number(), requiresPrescription: z.boolean(), lineTotalMinor: z.number(),
  })),
  currency: z.string(),
  subtotalMinor: z.number(),
  deliveryFeeMinor: z.number(),
  serviceFeeMinor: z.number(),
  rxHandlingFeeMinor: z.number(),
  totalMinor: z.number(),
  holdExpiresAt: z.string(),
  createdAt: z.string(),
  paidAt: z.string().optional(),
  cardBrand: z.string().optional(),
  cardLast4: z.string().optional(),
  refundMinor: z.number().optional(),
  rejectionReason: z.string().optional(),
  prescriptionId: z.string().optional(),
  canCancel: z.boolean(),
  refundIfCancelledMinor: z.number(),
});
export type Order = z.infer<typeof orderSchema>;

export const prescriptionSchema = z.object({
  id: z.string(),
  patientName: z.string(),
  prescriberName: z.string(),
  prescriberLicense: z.string().optional(),
  imageReference: z.string(),
  notes: z.string().optional(),
  issuedOn: z.string(),
  submittedAt: z.string(),
  isWithinValidity: z.boolean(),
});
export type Prescription = z.infer<typeof prescriptionSchema>;

export const alertSchema = z.object({ drugA: z.string(), drugB: z.string(), severity: z.string(), effect: z.string(), management: z.string() });
export type InteractionAlert = z.infer<typeof alertSchema>;

export const verificationItemSchema = z.object({
  order: orderSchema,
  customerName: z.string(),
  customerAllergies: z.string().optional(),
  customerAge: z.number(),
  prescription: prescriptionSchema.optional(),
  alerts: z.array(alertSchema),
  recentMedicines: z.array(z.string()),
});
export type VerificationItem = z.infer<typeof verificationItemSchema>;

export const careServiceSchema = z.object({
  id: z.string(), branchId: z.string(), name: z.string(), category: z.string(), description: z.string(),
  durationMinutes: z.number(), priceMinor: z.number(), currency: z.string(), minimumAge: z.number(), imageUrl: z.string(),
});
export type CareService = z.infer<typeof careServiceSchema>;

export const slotSchema = z.object({ slotId: z.string(), start: z.string(), spotsLeft: z.number() });
export type Slot = z.infer<typeof slotSchema>;

export const appointmentSchema = z.object({
  id: z.string(), serviceId: z.string(), serviceName: z.string(), branchId: z.string(), start: z.string(),
  status: z.enum(['booked', 'cancelled', 'completed', 'no_show']), priceMinor: z.number(), notes: z.string().optional(), canCancel: z.boolean(),
});
export type Appointment = z.infer<typeof appointmentSchema>;

export const dashboardSchema = z.object({
  salesTodayMinor: z.number(), sales7dMinor: z.number(), ordersToday: z.number(), pendingVerification: z.number(),
  lowStockItems: z.number(), expiringWithin60Days: z.number(), ordersByStatus: z.record(z.string(), z.number()),
});
export type Dashboard = z.infer<typeof dashboardSchema>;

export const inventoryRowSchema = z.object({
  branchId: z.string(), productId: z.string(), productName: z.string(), onHand: z.number(), reserved: z.number(),
  available: z.number(), reorderLevel: z.number(), nearestExpiry: z.string().optional(), isLow: z.boolean(),
});
export type InventoryRow = z.infer<typeof inventoryRowSchema>;

export const copilotResponseSchema = z.object({
  answer: z.string(),
  citations: z.array(z.object({ sourceId: z.string(), title: z.string(), quote: z.string(), provenance: z.string() })),
  alerts: z.array(alertSchema),
  sources: z.array(z.object({ id: z.string(), title: z.string() })),
  aiAvailable: z.boolean(),
});
export type CopilotResponse = z.infer<typeof copilotResponseSchema>;

export const assistantResponseSchema = z.object({
  answer: z.string(),
  toolCalls: z.array(z.object({ name: z.string(), input: z.string() })),
  aiAvailable: z.boolean(),
});
export type AssistantResponse = z.infer<typeof assistantResponseSchema>;

export const problemSchema = z.looseObject({ title: z.string().optional(), detail: z.string().optional(), code: z.string().optional() });
