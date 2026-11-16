import { router } from 'expo-router';
import { useState } from 'react';

import { useSession } from '@/core/auth/session';
import { useSubmitPrescription } from '@/features/account';
import { useI18n } from '@/i18n';
import { useTheme } from '@/theme';
import { Button, Card, ErrorBox, Field, Icon, Row, Screen, T } from '@/ui/kit';

export default function NewPrescriptionScreen() {
  const { user } = useSession();
  const { t } = useI18n();
  const { c } = useTheme();
  const submit = useSubmitPrescription();
  const today = new Date().toISOString().slice(0, 10);
  const [form, setForm] = useState({ patientName: user ? `${user.firstName} ${user.lastName}` : '', prescriberName: '', prescriberLicense: '', issuedOn: today, notes: '' });
  const set = (k: keyof typeof form) => (v: string) => setForm((f) => ({ ...f, [k]: v }));

  return (
    <Screen>
      <T tone="muted">{t('rx.intro')}</T>
      <Card tone="soft">
        <Row><Icon name="camera-outline" color={c.primary} /><T v="label">{t('rx.photo')}</T></Row>
        {/* Production: expo-image-picker -> direct upload to blob storage with a short-lived SAS URL; only the reference is sent to the API. */}
        <T v="small" tone="muted">{t('rx.photoNote')}</T>
      </Card>
      <Field label={t('rx.patient')} value={form.patientName} onChangeText={set('patientName')} autoComplete="name" />
      <Field label={t('rx.prescriber')} value={form.prescriberName} onChangeText={set('prescriberName')} />
      <Field label={`${t('rx.license')} (${t('common.optional')})`} value={form.prescriberLicense} onChangeText={set('prescriberLicense')} />
      <Field label={t('rx.issued')} value={form.issuedOn} onChangeText={set('issuedOn')} keyboardType="numbers-and-punctuation" />
      <Field label={`${t('rx.notes')} (${t('common.optional')})`} value={form.notes} onChangeText={set('notes')} multiline />
      {submit.isError ? <ErrorBox error={submit.error} /> : null}
      <Button
        title={t('rx.submit')}
        icon="cloud-upload-outline"
        loading={submit.isPending}
        disabled={!form.patientName.trim() || !form.prescriberName.trim()}
        onPress={() => submit.mutate(
          { ...form, prescriberLicense: form.prescriberLicense.trim() || undefined, notes: form.notes.trim() || undefined, imageReference: `rx-images/${Date.now()}.jpg` },
          { onSuccess: () => router.back() },
        )}
      />
    </Screen>
  );
}
