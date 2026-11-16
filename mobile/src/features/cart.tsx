import AsyncStorage from '@react-native-async-storage/async-storage';
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';

/**
 * Cart: client-side intent only. Prices shown here are a preview; the server re-prices from the live
 * catalogue at quote and order time. Persisted so an app restart doesn't lose the basket.
 */
export interface CartLine {
  productId: string;
  name: string;
  priceMinor: number;
  imageUrl: string;
  requiresPrescription: boolean;
  maxQty: number;
  quantity: number;
}

interface CartState {
  lines: CartLine[];
  branchId: string;
  fulfilment: 'pickup' | 'delivery';
}

interface CartApi extends CartState {
  count: number;
  subtotalMinor: number;
  needsPrescription: boolean;
  add: (line: Omit<CartLine, 'quantity'>, quantity?: number) => void;
  setQuantity: (productId: string, quantity: number) => void;
  remove: (productId: string) => void;
  clear: () => void;
  setBranch: (branchId: string) => void;
  setFulfilment: (f: 'pickup' | 'delivery') => void;
}

const KEY = 'medora.cart.v1';
const initial: CartState = { lines: [], branchId: 'brn_downtown', fulfilment: 'pickup' };
const CartContext = createContext<CartApi | null>(null);

export function CartProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<CartState>(initial);
  const [hydrated, setHydrated] = useState(false);

  useEffect(() => {
    AsyncStorage.getItem(KEY)
      .then((raw) => raw && setState({ ...initial, ...(JSON.parse(raw) as CartState) }))
      .catch(() => undefined)
      .finally(() => setHydrated(true));
  }, []);

  useEffect(() => {
    if (hydrated) AsyncStorage.setItem(KEY, JSON.stringify(state)).catch(() => undefined);
  }, [state, hydrated]);

  const value = useMemo<CartApi>(() => {
    const clamp = (q: number, max: number) => Math.max(1, Math.min(q, max, 10));
    return {
      ...state,
      count: state.lines.reduce((n, l) => n + l.quantity, 0),
      subtotalMinor: state.lines.reduce((n, l) => n + l.priceMinor * l.quantity, 0),
      needsPrescription: state.lines.some((l) => l.requiresPrescription),
      add: (line, quantity = 1) =>
        setState((s) => {
          const existing = s.lines.find((l) => l.productId === line.productId);
          const lines = existing
            ? s.lines.map((l) => (l.productId === line.productId ? { ...l, quantity: clamp(l.quantity + quantity, line.maxQty) } : l))
            : [...s.lines, { ...line, quantity: clamp(quantity, line.maxQty) }];
          return { ...s, lines };
        }),
      setQuantity: (productId, quantity) =>
        setState((s) => ({ ...s, lines: s.lines.map((l) => (l.productId === productId ? { ...l, quantity: clamp(quantity, l.maxQty) } : l)) })),
      remove: (productId) => setState((s) => ({ ...s, lines: s.lines.filter((l) => l.productId !== productId) })),
      clear: () => setState((s) => ({ ...s, lines: [] })),
      setBranch: (branchId) => setState((s) => ({ ...s, branchId })),
      setFulfilment: (fulfilment) => setState((s) => ({ ...s, fulfilment })),
    };
  }, [state]);

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>;
}

export function useCart() {
  const ctx = useContext(CartContext);
  if (!ctx) throw new Error('useCart must be used inside CartProvider');
  return ctx;
}
