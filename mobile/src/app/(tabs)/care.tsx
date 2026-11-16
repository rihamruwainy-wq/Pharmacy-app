import { Image } from 'expo-image';
import { router } from 'expo-router';
import { View } from 'react-native';

import { useBranches } from '@/features/catalog';
import { useCareServices } from '@/features/orders';
import { useI18n } from '@/i18n';
import { useTheme } from '@/theme';
import { Badge, Card, ErrorBox, Icon, Loading, Ornament, Row, Screen, T } from '@/ui/kit';

export default function CareScreen() {
  const { t, money, number } = useI18n();
  const { c, radius } = useTheme();
  const services = useCareServices();
  const branches = useBranches();
  const branchName = (id: string) => branches.data?.find((b) => b.id === id)?.name ?? '';

  return (
    <Screen refreshControl={undefined}>
      <T tone="muted">{t('care.subtitle')}</T>
      <Ornament />
      {services.isPending ? <Loading /> : services.isError ? <ErrorBox error={services.error} onRetry={() => services.refetch()} /> : services.data.map((s) => {
        const price = s.priceMinor === 0 ? t('common.free') : money(s.priceMinor);
        return (
          <Card key={s.id} onPress={() => router.push(`/service/${s.id}`)} label={`${s.name}, ${price}, ${branchName(s.branchId)}`}>
            <Row style={{ alignItems: 'flex-start', gap: 14 }}>
              <Image source={s.imageUrl} accessible={false} style={{ width: 88, height: 88, borderRadius: radius.md, backgroundColor: c.surfaceAlt }} contentFit="cover" cachePolicy="memory-disk" />
              <View style={{ flex: 1, gap: 6 }}>
                <T v="h2">{s.name}</T>
                <Row wrap gap={6}>
                  <Badge label={t('care.minutes', { count: number(s.durationMinutes) })} tone="neutral" icon="time-outline" />
                  <Badge label={price} tone={s.priceMinor === 0 ? 'ok' : 'info'} />
                </Row>
                <Row gap={4}><Icon name="location-outline" size={16} color={c.textMuted} /><T v="small" tone="muted">{branchName(s.branchId)}</T></Row>
              </View>
              <Icon name="chevron-forward" color={c.textMuted} directional />
            </Row>
          </Card>
        );
      })}
    </Screen>
  );
}
