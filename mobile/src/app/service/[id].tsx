import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useMemo, useState } from 'react';

import { useSession } from '@/core/auth/session';
import { useBook, useCareServices, useSlots } from '@/features/orders';
import { useI18n } from '@/i18n';
import { useTheme } from '@/theme';
import { Badge, Button, Card, Chip, Empty, ErrorBox, Field, Icon, Loading, Ornament, Row, Screen, T } from '@/ui/kit';

export default function ServiceScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { t, money, number, time, dayLabel } = useI18n();
  const { c } = useTheme();
  const services = useCareServices();
  const slots = useSlots(id);
  const book = useBook(id);
  const { user } = useSession();
  const [slotId, setSlotId] = useState<string>();
  const [notes, setNotes] = useState('');
  const service = services.data?.find((s) => s.id === id);

  const days = useMemo(() => {
    const groups = new Map<string, { slotId: string; start: string; spotsLeft: number }[]>();
    for (const s of slots.data ?? []) {
      if (s.spotsLeft <= 0) continue;
      const day = s.start.slice(0, 10);
      groups.set(day, [...(groups.get(day) ?? []), s]);
    }
    return [...groups.entries()];
  }, [slots.data]);

  if (!service) return services.isPending ? <Loading /> : <Screen><Empty title={t('error.NOT_FOUND')} /></Screen>;
  const price = service.priceMinor === 0 ? t('common.free') : t('care.payInStore', { price: money(service.priceMinor) });
  const selected = slots.data?.find((s) => s.slotId === slotId);

  return (
    <Screen>
      <Stack.Screen options={{ title: service.name }} />
      <T v="title">{service.name}</T>
      <T tone="muted">{service.description}</T>
      <Row wrap>
        <Badge label={t('care.minutes', { count: number(service.durationMinutes) })} tone="neutral" icon="time-outline" />
        <Badge label={price} tone="info" icon="cash-outline" />
        <Badge label={t('care.ages', { age: number(service.minimumAge) })} tone="neutral" icon="person-outline" />
      </Row>
      <Ornament />

      {book.isSuccess ? (
        <Card tone="soft">
          <Row><Icon name="checkmark-circle" color={c.primary} size={28} /><T v="h2">{t('care.booked')}</T></Row>
          <T>{dayLabel(book.data.start)} · {time(book.data.start)}</T>
          <T tone="muted">{t('care.bookedBody')}</T>
          <Button title={t('tabs.orders')} kind="secondary" onPress={() => router.replace('/orders')} />
        </Card>
      ) : (
        <>
          <T v="h2">{t('care.chooseTime')}</T>
          {slots.isPending ? <Loading /> : slots.isError ? <ErrorBox error={slots.error} onRetry={() => slots.refetch()} /> : days.length === 0 ? (
            <Empty icon="calendar-outline" title={t('care.noTimes')} />
          ) : days.map(([day, times]) => (
            <Card key={day}>
              <T v="label">{dayLabel(times[0]!.start)}</T>
              <Row wrap>
                {times.map((s) => <Chip key={s.slotId} label={time(s.start)} selected={slotId === s.slotId} onPress={() => setSlotId(s.slotId)} />)}
              </Row>
            </Card>
          ))}
          {selected ? <T v="small" tone="muted">{t('care.spots', { count: selected.spotsLeft })}</T> : null}
          <Field label={`${t('care.notes')} (${t('common.optional')})`} hint={t('care.notesHint')} value={notes} onChangeText={setNotes} multiline />
          {book.isError ? <ErrorBox error={book.error} /> : null}
          <Button
            title={user ? t('care.book') : t('care.loginToBook')}
            icon={user ? 'calendar-outline' : 'log-in-outline'}
            disabled={!!user && !slotId}
            loading={book.isPending}
            onPress={() => (user ? slotId && book.mutate({ slotId, notes: notes.trim() || undefined }) : router.push('/login'))}
          />
        </>
      )}
    </Screen>
  );
}
