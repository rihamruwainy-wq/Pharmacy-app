import { Image } from 'expo-image';
import { router } from 'expo-router';
import { useMemo, useRef, useState } from 'react';
import { View } from 'react-native';

import { useSession } from '@/core/auth/session';
import { usePrescriptions } from '@/features/account';
import { useCart } from '@/features/cart';
import { useBranches } from '@/features/catalog';
import { newIdempotencyKey, usePlaceOrder, useQuote } from '@/features/orders';
import { useI18n } from '@/i18n';
import { useTheme } from '@/theme';
import { Badge, Button, Card, Empty, ErrorBox, Field, Icon, MoneyRow, Ornament, Row, Screen, Segmented, Stepper, T } from '@/ui/kit';

export default function CartScreen() {
  const cart = useCart();
  const { user } = useSession();
  const { t, money, date } = useI18n();
  const { c, radius } = useTheme();
  const branches = useBranches();
  const prescriptions = usePrescriptions();
  const [address, setAddress] = useState('');
  const [prescriptionId, setPrescriptionId] = useState<string | undefined>();
  const place = usePlaceOrder();
  // One idempotency key per checkout intent: a retry/double-tap reuses it; any basket change starts a new intent.
  const intent = useRef<{ key: string; fingerprint: string } | null>(null);

  const lines = useMemo(() => cart.lines.map((l) => ({ productId: l.productId, quantity: l.quantity })), [cart.lines]);
  const quote = useQuote(cart.branchId, cart.fulfilment, lines, !!user);
  const validRx = (prescriptions.data ?? []).filter((p) => p.isWithinValidity);

  if (cart.lines.length === 0) {
    return <Screen><Empty icon="basket-outline" title={t('cart.empty')} body={t('cart.emptyBody')} action={t('cart.goShopping')} onAction={() => router.replace('/')} /></Screen>;
  }

  const placeOrder = () => {
    if (!user) return router.push('/login');
    const body = { branchId: cart.branchId, fulfilment: cart.fulfilment, deliveryAddress: cart.fulfilment === 'delivery' ? address : undefined, lines, prescriptionId };
    const fingerprint = JSON.stringify(body);
    if (intent.current?.fingerprint !== fingerprint) intent.current = { key: newIdempotencyKey(), fingerprint };
    place.mutate({ key: intent.current.key, ...body }, {
      onSuccess: (order) => {
        cart.clear();
        intent.current = null;
        router.replace(`/checkout/${order.id}`);
      },
    });
  };

  const blocked = (cart.needsPrescription && !prescriptionId) || (cart.fulfilment === 'delivery' && address.trim().length < 5);

  return (
    <Screen>
      {cart.lines.map((l) => (
        <Card key={l.productId}>
          <Row style={{ alignItems: 'flex-start' }}>
            <Image source={l.imageUrl} accessible={false} style={{ width: 64, height: 64, borderRadius: radius.md, backgroundColor: c.surfaceAlt }} contentFit="cover" cachePolicy="memory-disk" />
            <View style={{ flex: 1, gap: 4 }}>
              <T v="label">{l.name}</T>
              {l.requiresPrescription ? <Badge label={t('cart.needsRx')} tone="danger" icon="document-text-outline" /> : null}
              <T weight="semibold" tone="accent">{money(l.priceMinor * l.quantity)}</T>
            </View>
          </Row>
          <Row style={{ justifyContent: 'space-between' }}>
            <Stepper value={l.quantity} max={l.maxQty} onChange={(q) => cart.setQuantity(l.productId, q)} label={`${t('product.quantity')}: ${l.name}`} />
            <Button title={t('common.remove')} kind="ghost" icon="trash-outline" compact onPress={() => cart.remove(l.productId)} />
          </Row>
        </Card>
      ))}

      <Ornament />
      <Card>
        <T v="h2">{t('cart.pharmacy')}</T>
        <Segmented label={t('cart.pharmacy')} value={cart.branchId} onChange={cart.setBranch}
          options={(branches.data ?? []).map((b) => ({ value: b.id, label: `${b.name} · ${b.city}`, icon: 'storefront-outline' as const }))} />
      </Card>

      <Card>
        <T v="h2">{t('cart.receive')}</T>
        <Segmented label={t('cart.receive')} value={cart.fulfilment} onChange={cart.setFulfilment}
          options={[{ value: 'pickup', label: t('cart.pickup'), icon: 'walk-outline' }, { value: 'delivery', label: t('cart.delivery'), icon: 'bicycle-outline' }]} />
        {cart.fulfilment === 'delivery' ? <Field label={t('cart.address')} hint={t('cart.addressHint')} value={address} onChangeText={setAddress} autoComplete="street-address" textContentType="fullStreetAddress" /> : null}
      </Card>

      {cart.needsPrescription ? (
        <Card tone="warn">
          <Row><Icon name="document-text-outline" color={c.warn} /><T v="h2">{t('cart.prescription')}</T></Row>
          {!user ? <T tone="muted">{t('cart.loginForRx')}</T> : validRx.length === 0 ? <T tone="muted">{t('cart.noRx')}</T> : (
            <Segmented label={t('cart.prescription')} value={prescriptionId ?? ''} onChange={setPrescriptionId}
              options={validRx.map((p) => ({ value: p.id, label: t('cart.issuedBy', { doctor: p.prescriberName, date: date(p.issuedOn) }) }))} />
          )}
          {user ? <Button title={t('cart.addRx')} kind="secondary" icon="add" onPress={() => router.push('/rx/new')} /> : null}
        </Card>
      ) : null}

      <Card>
        {!user ? <T tone="muted">{t('cart.loginForTotal')}</T> : quote.data ? (
          <>
            <MoneyRow label={t('cart.subtotal')} minor={quote.data.subtotalMinor} />
            <MoneyRow label={t('cart.deliveryFee')} minor={quote.data.deliveryFeeMinor} />
            <MoneyRow label={t('cart.serviceFee')} minor={quote.data.serviceFeeMinor} />
            {quote.data.rxHandlingFeeMinor > 0 ? <MoneyRow label={t('cart.rxFee')} minor={quote.data.rxHandlingFeeMinor} /> : null}
            <Ornament />
            <MoneyRow label={t('cart.total')} minor={quote.data.totalMinor} bold />
            {cart.fulfilment === 'delivery' && quote.data.deliveryFeeMinor > 0 ? <T v="small" tone="muted">{t('cart.freeDelivery')}</T> : null}
          </>
        ) : quote.isError ? <ErrorBox error={quote.error} onRetry={() => quote.refetch()} /> : <T tone="muted">{t('cart.calculating')}</T>}
      </Card>

      {place.isError ? <ErrorBox error={place.error} /> : null}
      <Button title={user ? t('cart.reserve') : t('cart.loginToCheckout')} icon={user ? 'lock-closed-outline' : 'log-in-outline'} onPress={placeOrder} loading={place.isPending} disabled={!!user && blocked} />
      <Row style={{ justifyContent: 'center' }}><Icon name="time-outline" size={16} color={c.textMuted} /><T v="small" tone="muted">{t('cart.holdNote')}</T></Row>
    </Screen>
  );
}
