import { useState } from 'react';
import { View } from 'react-native';

import { useDashboard, useInventory, useRestock } from '@/features/staff';
import { useI18n } from '@/i18n';
import { useTheme } from '@/theme';
import { Badge, Button, Card, Chip, ErrorBox, Icon, Loading, Row, Screen, SectionHeader, T, type IconName } from '@/ui/kit';

const RESTOCK = 24;

function Kpi({ label, value, icon, tone }: { label: string; value: string; icon: IconName; tone?: 'danger' | 'warn' }) {
  const { c } = useTheme();
  const color = tone === 'danger' ? c.danger : tone === 'warn' ? c.warn : c.primary;
  return (
    <Card style={{ flexGrow: 1, flexBasis: 150 }} label={`${label}: ${value}`}>
      <Row><Icon name={icon} size={18} color={color} /><T v="small" tone="muted" style={{ flexShrink: 1 }}>{label}</T></Row>
      <T v="title" style={{ color }}>{value}</T>
    </Card>
  );
}

export default function DashboardScreen() {
  const { t, money, number, status, date } = useI18n();
  const dash = useDashboard();
  const [lowOnly, setLowOnly] = useState(true);
  const inventory = useInventory({ lowOnly });
  const restock = useRestock();

  return (
    <Screen>
      <SectionHeader title={t('dash.today')} />
      {dash.isPending ? <Loading /> : dash.isError ? <ErrorBox error={dash.error} onRetry={() => dash.refetch()} /> : (
        <>
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: 10 }}>
            <Kpi label={t('dash.salesToday')} value={money(dash.data.salesTodayMinor)} icon="cash-outline" />
            <Kpi label={t('dash.sales7d')} value={money(dash.data.sales7dMinor)} icon="trending-up-outline" />
            <Kpi label={t('dash.rxPending')} value={number(dash.data.pendingVerification)} icon="document-text-outline" tone={dash.data.pendingVerification ? 'warn' : undefined} />
            <Kpi label={t('dash.lowStock')} value={number(dash.data.lowStockItems)} icon="alert-circle-outline" tone={dash.data.lowStockItems ? 'danger' : undefined} />
            <Kpi label={t('dash.expiring')} value={number(dash.data.expiringWithin60Days)} icon="hourglass-outline" />
          </View>
          <Card>
            <T v="label">{t('dash.byStatus')}</T>
            <Row wrap>{Object.entries(dash.data.ordersByStatus).map(([k, n]) => <Badge key={k} tone="neutral" label={`${status(k)}: ${number(n)}`} />)}</Row>
          </Card>
        </>
      )}

      <SectionHeader title={t('dash.inventory')} />
      <Row>
        <Chip label={t('dash.lowOnly')} selected={lowOnly} onPress={() => setLowOnly(true)} />
        <Chip label={t('common.all')} selected={!lowOnly} onPress={() => setLowOnly(false)} />
      </Row>
      {restock.isError ? <ErrorBox error={restock.error} /> : null}
      {inventory.isPending ? <Loading /> : inventory.isError ? <ErrorBox error={inventory.error} /> : inventory.data.items.map((r) => {
        const tone = r.available <= 0 ? 'danger' : r.isLow ? 'warn' : 'ok';
        return (
          <Card key={`${r.branchId}/${r.productId}`}>
            <Row style={{ justifyContent: 'space-between' }}>
              <T v="label" numberOfLines={1} style={{ flex: 1 }}>{r.productName}</T>
              <Badge label={r.available <= 0 ? t('dash.out') : r.isLow ? t('dash.low') : t('dash.ok')} tone={tone} />
            </Row>
            <T v="small" tone="muted">{t('dash.stockLine', { branch: r.branchId.replace('brn_', ''), available: number(r.available), onHand: number(r.onHand), reserved: number(r.reserved), reorder: number(r.reorderLevel) })}</T>
            {r.nearestExpiry ? <T v="caption" tone="muted">{t('dash.expires', { date: date(r.nearestExpiry) })}</T> : null}
            <Button title={t('dash.receive', { count: number(RESTOCK) })} kind="secondary" compact icon="add-circle-outline"
              loading={restock.isPending && restock.variables?.productId === r.productId && restock.variables?.branchId === r.branchId}
              onPress={() => restock.mutate({ branchId: r.branchId, productId: r.productId, quantity: RESTOCK })} />
          </Card>
        );
      })}
    </Screen>
  );
}
