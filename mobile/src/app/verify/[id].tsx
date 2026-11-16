import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';

import { shortId } from '@/core/format';
import { useVerification, useVerify } from '@/features/staff';
import { useI18n } from '@/i18n';
import { useTheme } from '@/theme';
import { CopilotAsk } from '@/ui/ai';
import { AlertCard, Badge, Button, Card, ErrorBox, Field, Icon, Loading, Ornament, Row, Screen, SectionHeader, T } from '@/ui/kit';

export default function VerifyScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { t, money, date } = useI18n();
  const { c } = useTheme();
  const item = useVerification(id);
  const verify = useVerify(id);
  const [reason, setReason] = useState('');
  const [rejecting, setRejecting] = useState(false);

  if (item.isPending) return <Loading />;
  if (item.isError) return <Screen><ErrorBox error={item.error} onRetry={() => item.refetch()} /></Screen>;
  const v = item.data;
  const pending = v.order.status === 'awaiting_verification';

  return (
    <Screen>
      <Row style={{ justifyContent: 'space-between' }}>
        <T v="title">{t('order.title', { id: shortId(v.order.id) })}</T>
        <Badge label={pending ? t('staff.needsVerification') : t(`status.${v.order.status}` as never)} tone={pending ? 'warn' : 'ok'} />
      </Row>
      <Card>
        <Row><Icon name="person-outline" color={c.primary} /><T v="h2">{t('staff.patient', { name: v.customerName, age: v.customerAge })}</T></Row>
        <T weight={v.customerAllergies ? 'semibold' : 'regular'} tone={v.customerAllergies ? 'danger' : 'muted'}>{t('staff.allergies', { value: v.customerAllergies ?? t('staff.noneDeclared') })}</T>
        <T tone="muted">{t('staff.recent', { value: v.recentMedicines.length ? v.recentMedicines.join('، ') : t('staff.none') })}</T>
      </Card>

      <SectionHeader title={t('staff.alertsTitle', { count: v.alerts.length })} />
      {v.alerts.length === 0 ? <Card><T tone="muted">{t('staff.noAlertsBody')}</T></Card> : v.alerts.map((a) => <AlertCard key={`${a.drugA}-${a.drugB}`} alert={a} />)}

      <Card>
        <T v="h2">{t('staff.items')}</T>
        {v.order.lines.map((l) => (
          <Row key={l.productId} style={{ justifyContent: 'space-between' }}>
            <T style={{ flex: 1 }}>{l.quantity} × {l.name}</T>
            {l.requiresPrescription ? <Badge label="Rx" tone="danger" /> : null}
          </Row>
        ))}
        <T v="small" tone="muted">{v.order.fulfilment === 'pickup' ? t('cart.pickup') : t('cart.delivery')} · {money(v.order.totalMinor)}</T>
      </Card>

      <Card>
        <T v="h2">{t('staff.prescription')}</T>
        {v.prescription ? (
          <>
            <T>{v.prescription.prescriberName}{v.prescription.prescriberLicense ? ` (${v.prescription.prescriberLicense})` : ''}</T>
            <T tone="muted">{t('staff.rxPatient', { name: v.prescription.patientName, date: date(v.prescription.issuedOn) })}</T>
            {v.prescription.notes ? <T tone="muted">{t('staff.rxNotes', { notes: v.prescription.notes })}</T> : null}
            <Badge label={v.prescription.isWithinValidity ? t('staff.withinValidity') : t('account.expired')} tone={v.prescription.isWithinValidity ? 'ok' : 'danger'} />
          </>
        ) : <T tone="muted">{t('staff.noRx')}</T>}
      </Card>

      <Ornament />
      <CopilotAsk orderId={v.order.id} />
      <Ornament />

      {pending ? (
        <Card>
          {verify.isError ? <ErrorBox error={verify.error} /> : null}
          {rejecting ? (
            <>
              <Field label={t('staff.reason')} placeholder={t('staff.reasonHint')} value={reason} onChangeText={setReason} multiline />
              <Button title={t('staff.rejectRefund')} kind="danger" icon="close-circle-outline" disabled={reason.trim().length < 5} loading={verify.isPending}
                onPress={() => verify.mutate({ approve: false, reason: reason.trim() }, { onSuccess: () => router.back() })} />
              <Button title={t('common.back')} kind="ghost" onPress={() => setRejecting(false)} />
            </>
          ) : (
            <Row>
              <Button title={t('staff.reject')} kind="ghost" icon="close-outline" onPress={() => setRejecting(true)} style={{ flex: 1 }} />
              <Button title={t('staff.approve')} icon="checkmark" loading={verify.isPending} style={{ flex: 1.4 }} onPress={() => verify.mutate({ approve: true }, { onSuccess: () => router.back() })} />
            </Row>
          )}
          <T v="caption" tone="muted">{t('staff.audit')}</T>
        </Card>
      ) : null}
    </Screen>
  );
}
