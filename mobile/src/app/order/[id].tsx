import { router, Stack, useLocalSearchParams } from 'expo-router';
import { View } from 'react-native';

import type { Order } from '@/core/api/schemas';
import { shortId } from '@/core/format';
import { useCancelOrder, useOrder } from '@/features/orders';
import { useI18n } from '@/i18n';
import { useTheme } from '@/theme';
import { Badge, Button, Card, ErrorBox, Icon, Loading, MoneyRow, Ornament, Row, Screen, T } from '@/ui/kit';

const FLOW = ['awaiting_verification', 'preparing', 'ready_for_pickup', 'out_for_delivery', 'completed'] as const;
const BAD: Order['status'][] = ['cancelled', 'rejected', 'expired'];

export default function OrderScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { t, money, dateTime, status } = useI18n();
  const { c } = useTheme();
  const order = useOrder(id);
  const cancel = useCancelOrder(id);

  if (order.isPending) return <Loading />;
  if (order.isError) return <Screen><ErrorBox error={order.error} onRetry={() => order.refetch()} /></Screen>;
  const o = order.data;
  const steps = FLOW.filter((s) => (o.fulfilment === 'pickup' ? s !== 'out_for_delivery' : s !== 'ready_for_pickup'))
    .filter((s) => s !== 'awaiting_verification' || o.lines.some((l) => l.requiresPrescription));
  const reached = steps.indexOf(o.status as (typeof FLOW)[number]);
  const bad = BAD.includes(o.status);

  return (
    <Screen>
      <Stack.Screen options={{ title: t('order.title', { id: shortId(o.id) }) }} />
      <Row style={{ justifyContent: 'space-between' }}>
        <T v="title">{t('order.title', { id: shortId(o.id) })}</T>
        <Badge label={status(o.status)} tone={bad ? 'danger' : o.status === 'completed' ? 'ok' : 'warn'} />
      </Row>
      <T tone="muted">{t('order.placed', { date: dateTime(o.createdAt) })} · {o.fulfilment === 'pickup' ? t('order.pickup') : t('order.deliveryTo', { address: o.deliveryAddress ?? '' })}</T>

      {!bad && o.status !== 'pending_payment' ? (
        <Card>
          <T v="h2">{t('order.progress')}</T>
          {steps.map((s, i) => {
            const done = i <= reached;
            return (
              <Row key={s} style={{ alignItems: 'flex-start' }}>
                <View style={{ alignItems: 'center', width: 26 }}>
                  <Icon name={done ? 'checkmark-circle' : 'ellipse-outline'} color={done ? c.primary : c.border} size={24} />
                  {i < steps.length - 1 ? <View style={{ width: 2, height: 18, backgroundColor: i < reached ? c.primary : c.border }} /> : null}
                </View>
                <T weight={i === reached ? 'bold' : 'regular'} tone={done ? 'default' : 'muted'}>{status(s)}</T>
              </Row>
            );
          })}
          <T v="small" tone="muted">{t('order.autoRefresh')}</T>
        </Card>
      ) : null}

      {o.status === 'rejected' && o.rejectionReason ? (
        <Card tone="danger"><T v="label">{t('order.pharmacistNote')}</T><T>{o.rejectionReason}</T></Card>
      ) : null}

      <Card>
        <T v="h2">{t('order.items')}</T>
        {o.lines.map((l) => <MoneyRow key={l.productId} label={`${l.quantity} × ${l.name}`} minor={l.lineTotalMinor} />)}
        <MoneyRow label={t('cart.deliveryFee')} minor={o.deliveryFeeMinor} />
        <MoneyRow label={t('cart.serviceFee')} minor={o.serviceFeeMinor} />
        {o.rxHandlingFeeMinor > 0 ? <MoneyRow label={t('cart.rxFee')} minor={o.rxHandlingFeeMinor} /> : null}
        <Ornament />
        <MoneyRow label={t('cart.total')} minor={o.totalMinor} bold />
        {o.cardLast4 ? <T v="small" tone="muted">{t('order.paidWith', { brand: o.cardBrand ?? '', last4: o.cardLast4 })}</T> : null}
        {o.refundMinor ? <Badge tone="ok" icon="return-down-back-outline" label={t('order.refunded', { amount: money(o.refundMinor) })} /> : null}
      </Card>

      {o.status === 'pending_payment' ? <Button title={t('order.continuePay')} icon="card-outline" onPress={() => router.replace(`/checkout/${o.id}`)} /> : null}
      {o.canCancel ? (
        <Card>
          <T>
            {o.status === 'pending_payment' ? t('order.cancelPending')
              : o.refundIfCancelledMinor === o.totalMinor ? t('order.cancelFull', { amount: money(o.refundIfCancelledMinor) })
              : t('order.cancelPartial', { amount: money(o.refundIfCancelledMinor) })}
          </T>
          {cancel.isError ? <ErrorBox error={cancel.error} /> : null}
          <Button title={t('order.cancel')} kind="danger" icon="close-circle-outline" onPress={() => cancel.mutate()} loading={cancel.isPending} />
        </Card>
      ) : null}
    </Screen>
  );
}
