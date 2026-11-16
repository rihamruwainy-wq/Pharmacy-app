import { router } from 'expo-router';
import { useState } from 'react';
import { Pressable, View } from 'react-native';

import { useSession } from '@/core/auth/session';
import { logout, usePrescriptions, useUpdateProfile } from '@/features/account';
import { useI18n, type Key } from '@/i18n';
import { touchTarget, useTheme } from '@/theme';
import { Badge, Button, Card, Empty, ErrorBox, Field, Icon, Ornament, Row, Screen, T, type IconName } from '@/ui/kit';

export default function AccountScreen() {
  const { user, isStaff } = useSession();
  const { t, date } = useI18n();
  const update = useUpdateProfile();
  const prescriptions = usePrescriptions();
  const [allergies, setAllergies] = useState<string | undefined>(undefined);

  return (
    <Screen>
      {!user ? (
        <Empty icon="person-circle-outline" title={t('account.welcome')} body={t('account.welcomeBody')} action={t('account.loginSignup')} onAction={() => router.push('/login')} />
      ) : (
        <>
          <T v="title">{t('account.hello', { name: user.firstName })}</T>
          <Row wrap>
            {user.roles.map((r) => <Badge key={r} label={t(`account.role.${r}` as Key)} tone={r === 'customer' ? 'ok' : 'info'} />)}
            <T v="small" tone="muted">{user.email}</T>
          </Row>
        </>
      )}

      <Card style={{ padding: 0, gap: 0 }}>
        <MenuRow icon="settings-outline" label={t('common.settings')} onPress={() => router.push('/settings')} />
      </Card>

      {user && !isStaff ? (
        <>
          <Ornament />
          <Card>
            <Row><Icon name="alert-circle-outline" /><T v="h2">{t('account.allergies')}</T></Row>
            <Field label={t('account.allergiesLabel')} hint={t('account.allergiesHint')} placeholder={t('account.allergiesPlaceholder')} value={allergies ?? user.allergies ?? ''} onChangeText={setAllergies} />
            {update.isError ? <ErrorBox error={update.error} /> : null}
            <Button title={update.isSuccess && allergies === undefined ? t('common.saved') : t('common.save')} kind="secondary" icon={update.isSuccess && allergies === undefined ? 'checkmark' : undefined} loading={update.isPending}
              onPress={() => update.mutate({ firstName: user.firstName, lastName: user.lastName, allergies: (allergies ?? user.allergies ?? '').trim() || undefined }, { onSuccess: () => setAllergies(undefined) })} />
          </Card>
          <Card>
            <Row style={{ justifyContent: 'space-between' }}>
              <Row><Icon name="document-text-outline" /><T v="h2">{t('account.prescriptions')}</T></Row>
              <Button title={t('common.add')} kind="secondary" icon="add" compact onPress={() => router.push('/rx/new')} />
            </Row>
            {(prescriptions.data ?? []).length === 0 ? <T tone="muted">{t('account.noRx')}</T> : prescriptions.data!.map((p) => (
              <Row key={p.id} style={{ justifyContent: 'space-between' }}>
                <T style={{ flex: 1 }}>{t('account.issued', { doctor: p.prescriberName, date: date(p.issuedOn) })}</T>
                <Badge label={p.isWithinValidity ? t('account.valid') : t('account.expired')} tone={p.isWithinValidity ? 'ok' : 'danger'} />
              </Row>
            ))}
          </Card>
        </>
      ) : null}

      {user && isStaff ? <Card><T tone="muted">{t('account.staff', { branch: user.branchId ? ` · ${user.branchId.replace('brn_', '')}` : '' })}</T></Card> : null}
      {user ? <Button title={t('common.logOut')} kind="ghost" icon="log-out-outline" onPress={() => logout()} /> : null}
    </Screen>
  );
}

function MenuRow({ icon, label, onPress }: { icon: IconName; label: string; onPress: () => void }) {
  const { c, spacing } = useTheme();
  return (
    <Pressable accessibilityRole="button" accessibilityLabel={label} onPress={onPress}
      style={({ pressed }) => [{ flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingHorizontal: spacing.lg, minHeight: touchTarget + 8 }, pressed && { backgroundColor: c.surfaceAlt }]}>
      <Icon name={icon} color={c.primary} />
      <View style={{ flex: 1 }}><T v="label">{label}</T></View>
      <Icon name="chevron-forward" color={c.textMuted} directional />
    </Pressable>
  );
}
