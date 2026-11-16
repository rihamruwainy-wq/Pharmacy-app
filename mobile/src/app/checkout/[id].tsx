import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';

import { ApiError } from '@/core/api/http';
import { TEST_PAYMENT_METHODS } from '@/core/config';
import { useOrder, usePay } from '@/features/orders';
import { useI18n, type Key } from '@/i18n';
import { useTheme } from '@/theme';
import { Badge, Button, Card, ErrorBox, Icon, Loading, MoneyRow, Ornament, Row, Screen, Segmented, T } from '@/ui/kit';

const NOTE: Record<string, Key> = {
  pm_card_visa: 'card.succeeds',
  pm_card_mastercard: 'card.succeeds',
  pm_card_chargeDeclined: 'card.declined',
  pm_card_chargeDeclinedInsufficientFunds: 'card.insufficient',
};

function useCountdown(until?: string) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);
  const ms = until ? Date.parse(until) - now : 0;
  const m = Math.max(0, Math.floor(ms / 60000));
  const s = Math.max(0, Math.floor((ms % 60000) / 1000));
  return { expired: ms <= 0, label: `${m}:${String(s).padStart(2, '0')}` };
}

export default function CheckoutScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { t, money } = useI18n();
  const { c } = useTheme();
  const order = useOrder(id);
  const pay = usePay(id);
  const [method, setMethod] = useState<string>(TEST_PAYMENT_METHODS[0].id);
  const countdown = useCountdown(order.data?.holdExpiresAt);

  useEffect(() => {
    if (order.data && order.data.status !== 'pending_payment') router.replace(`/order/${id}`);
  }, [order.data, id]);

  if (order.isPending) return <Loading />;
  if (order.isError) return <Screen><ErrorBox error={order.error} onRetry={() => order.refetch()} /></Screen>;
  const o = order.data;
  const expired = (pay.error instanceof ApiError && pay.error.code === 'HOLD_EXPIRED') || countdown.expired;

  return (
    <Screen>
      <Card tone={expired ? 'danger' : 'soft'}>
        <Row style={{ justifyContent: 'space-between' }}>
          <Row style={{ flexShrink: 1 }}><Icon name="lock-closed-outline" color={c.primary} /><T v="h2" style={{ flexShrink: 1 }}>{t('checkout.reserved')}</T></Row>
          {/* The timer is not a live region: announcing every second would be unbearable with a screen reader. */}
          <Badge label={expired ? t('checkout.expired') : t('checkout.timeLeft', { time: countdown.label })} tone={expired ? 'danger' : 'warn'} icon="time-outline" />
        </Row>
        <T tone="muted">{t('checkout.reservedBody')}</T>
      </Card>

      <Card>
        {o.lines.map((l) => <MoneyRow key={l.productId} label={`${l.quantity} × ${l.name}`} minor={l.lineTotalMinor} />)}
        <MoneyRow label={t('cart.deliveryFee')} minor={o.deliveryFeeMinor} />
        <MoneyRow label={t('cart.serviceFee')} minor={o.serviceFeeMinor} />
        {o.rxHandlingFeeMinor > 0 ? <MoneyRow label={t('cart.rxFee')} minor={o.rxHandlingFeeMinor} /> : null}
        <Ornament />
        <MoneyRow label={t('cart.total')} minor={o.totalMinor} bold />
      </Card>

      <Card>
        <Row><Badge label={t('checkout.testMode')} tone="info" icon="flask-outline" /><T v="label">{t('checkout.chooseCard')}</T></Row>
        <Segmented label={t('checkout.chooseCard')} value={method} onChange={setMethod}
          options={TEST_PAYMENT_METHODS.map((m) => ({ value: m.id, label: `${m.label} — ${t(NOTE[m.id] ?? 'card.succeeds')}`, icon: 'card-outline' as const }))} />
        <T v="small" tone="muted">{t('checkout.tokenNote')}</T>
      </Card>

      {pay.isError ? <ErrorBox error={pay.error} /> : null}
      {expired ? <Button title={t('checkout.backToShop')} kind="secondary" onPress={() => router.replace('/')} /> : null}
      <Button title={t('checkout.pay', { amount: money(o.totalMinor, o.currency) })} icon="shield-checkmark-outline" onPress={() => pay.mutate(method)} loading={pay.isPending} disabled={expired} />
    </Screen>
  );
}
