import { router } from 'expo-router';
import { RefreshControl } from 'react-native';

import type { Order } from '@/core/api/schemas';
import { useSession } from '@/core/auth/session';
import { shortId } from '@/core/format';
import { useCancelAppointment, useMyAppointments, useMyOrders } from '@/features/orders';
import { useI18n } from '@/i18n';
import { useTheme } from '@/theme';
import { Badge, Button, Card, Empty, ErrorBox, Icon, Loading, Row, Screen, SectionHeader, T } from '@/ui/kit';

const BAD: Order['status'][] = ['cancelled', 'rejected', 'expired'];

export default function OrdersScreen() {
  const { user } = useSession();
  const { t, money, dateTime, status } = useI18n();
  const { c } = useTheme();
  const orders = useMyOrders();
  const appointments = useMyAppointments();
  const cancel = useCancelAppointment();

  if (!user) return <Screen><Empty icon="receipt-outline" title={t('orders.loginTitle')} body={t('orders.loginBody')} action={t('common.logIn')} onAction={() => router.push('/login')} /></Screen>;
  const upcoming = (appointments.data ?? []).filter((a) => a.status === 'booked');

  return (
    <Screen refreshControl={<RefreshControl tintColor={c.primary} refreshing={orders.isRefetching} onRefresh={() => { void orders.refetch(); void appointments.refetch(); }} />}>
      {upcoming.length > 0 ? <SectionHeader title={t('orders.upcoming')} /> : null}
      {upcoming.map((a) => (
        <Card key={a.id}>
          <Row style={{ justifyContent: 'space-between' }}>
            <Row style={{ flexShrink: 1 }}><Icon name="calendar" color={c.primary} /><T v="label" style={{ flexShrink: 1 }}>{a.serviceName}</T></Row>
            <Badge label={status('booked')} tone="ok" />
          </Row>
          <T tone="muted">{dateTime(a.start)}</T>
          {a.canCancel ? <Button title={t('orders.cancelAppointment')} kind="ghost" compact icon="close-outline" onPress={() => cancel.mutate(a.id)} loading={cancel.isPending && cancel.variables === a.id} />
            : <T v="small" tone="muted">{t('orders.tooLate')}</T>}
        </Card>
      ))}

      <SectionHeader title={t('orders.history')} />
      {orders.isPending ? <Loading /> : orders.isError ? <ErrorBox error={orders.error} onRetry={() => orders.refetch()} /> : orders.data.length === 0 ? (
        <Empty icon="receipt-outline" title={t('orders.none')} body={t('orders.noneBody')} action={t('cart.goShopping')} onAction={() => router.replace('/')} />
      ) : orders.data.map((o) => (
        <Card key={o.id} onPress={() => router.push(`/order/${o.id}`)} label={`${t('order.title', { id: shortId(o.id) })}, ${status(o.status)}, ${money(o.totalMinor)}`}>
          <Row style={{ justifyContent: 'space-between' }}>
            <T v="label">{t('order.title', { id: shortId(o.id) })}</T>
            <Badge label={status(o.status)} tone={BAD.includes(o.status) ? 'danger' : o.status === 'completed' ? 'ok' : 'warn'} />
          </Row>
          <T tone="muted" numberOfLines={1}>{o.lines.map((l) => `${l.quantity}× ${l.name}`).join('، ')}</T>
          <Row style={{ justifyContent: 'space-between' }}>
            <T v="small" tone="muted">{dateTime(o.createdAt)}</T>
            <T weight="semibold" tone="accent">{money(o.totalMinor)}</T>
          </Row>
        </Card>
      ))}
    </Screen>
  );
}
