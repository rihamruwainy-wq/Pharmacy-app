import { router } from 'expo-router';
import { RefreshControl } from 'react-native';

import { shortId } from '@/core/format';
import { useActiveOrders, useAdvance, useQueue } from '@/features/staff';
import { useI18n, type Key } from '@/i18n';
import { useTheme } from '@/theme';
import { Badge, Button, Card, Empty, ErrorBox, Icon, Loading, Row, Screen, SectionHeader, T } from '@/ui/kit';

const NEXT: Record<string, { to: string; label: Key; only?: 'pickup' | 'delivery' }[]> = {
  preparing: [{ to: 'ready_for_pickup', label: 'staff.toReady', only: 'pickup' }, { to: 'out_for_delivery', label: 'staff.toDelivery', only: 'delivery' }],
  ready_for_pickup: [{ to: 'completed', label: 'staff.collected' }],
  out_for_delivery: [{ to: 'completed', label: 'staff.delivered' }],
};
const SERIOUS = ['allergy', 'contraindicated', 'major'];

export default function QueueScreen() {
  const { t, dateTime, status } = useI18n();
  const { c } = useTheme();
  const queue = useQueue();
  const active = useActiveOrders();
  const advance = useAdvance();

  return (
    <Screen refreshControl={<RefreshControl tintColor={c.primary} refreshing={queue.isRefetching} onRefresh={() => { void queue.refetch(); void active.refetch(); }} />}>
      <SectionHeader title={t('staff.queueTitle')} />
      {queue.isPending ? <Loading /> : queue.isError ? <ErrorBox error={queue.error} onRetry={() => queue.refetch()} /> : queue.data.length === 0 ? (
        <Empty icon="checkmark-done-outline" title={t('staff.queueClear')} />
      ) : queue.data.map((item) => {
        const serious = item.alerts.filter((a) => SERIOUS.includes(a.severity)).length;
        const alertLabel = serious > 0 ? t('staff.serious', { count: serious }) : item.alerts.length > 0 ? t('staff.alerts', { count: item.alerts.length }) : t('staff.noAlerts');
        return (
          <Card key={item.order.id} onPress={() => router.push(`/verify/${item.order.id}`)} label={`${item.customerName}, ${alertLabel}`}>
            <Row style={{ justifyContent: 'space-between' }}>
              <T v="label" style={{ flexShrink: 1 }}>{t('staff.patient', { name: item.customerName, age: item.customerAge })}</T>
              <Badge label={alertLabel} tone={serious > 0 ? 'danger' : item.alerts.length > 0 ? 'warn' : 'ok'} icon={serious > 0 ? 'warning' : undefined} />
            </Row>
            <T tone="muted" numberOfLines={2}>{item.order.lines.map((l) => `${l.quantity}× ${l.name}`).join('، ')}</T>
            <Row style={{ justifyContent: 'space-between' }}>
              <T v="small" tone="muted">{t('order.title', { id: shortId(item.order.id) })} · {item.order.paidAt ? t('staff.paid', { date: dateTime(item.order.paidAt) }) : ''}</T>
              <Icon name="chevron-forward" color={c.textMuted} directional />
            </Row>
          </Card>
        );
      })}

      <SectionHeader title={t('staff.inProgress')} />
      {active.isPending ? <Loading /> : (active.data ?? []).length === 0 ? <Empty icon="cube-outline" title={t('staff.nothing')} /> : active.data!.map((o) => (
        <Card key={o.id}>
          <Row style={{ justifyContent: 'space-between' }}>
            <T v="label">{t('order.title', { id: shortId(o.id) })} · {o.fulfilment === 'pickup' ? t('cart.pickup') : t('cart.delivery')}</T>
            <Badge label={status(o.status)} tone="warn" />
          </Row>
          <T tone="muted" numberOfLines={2}>{o.lines.map((l) => `${l.quantity}× ${l.name}`).join('، ')}</T>
          <Row wrap>
            {(NEXT[o.status] ?? []).filter((n) => !n.only || n.only === o.fulfilment).map((n) => (
              <Button key={n.to} title={t(n.label)} kind="secondary" compact icon="arrow-forward" loading={advance.isPending && advance.variables?.id === o.id} onPress={() => advance.mutate({ id: o.id, to: n.to })} />
            ))}
          </Row>
        </Card>
      ))}
      {advance.isError ? <ErrorBox error={advance.error} /> : null}
    </Screen>
  );
}
