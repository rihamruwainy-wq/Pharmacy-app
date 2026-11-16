import { router } from 'expo-router';
import { useState } from 'react';

import { login, lookupEmail, register } from '@/features/account';
import { useI18n } from '@/i18n';
import { useTheme } from '@/theme';
import { Button, Card, ErrorBox, Field, IconButton, Ornament, Screen, T } from '@/ui/kit';

/** One "continue with email" flow (Airbnb pattern): look up the email, then log in or finish signing up. */
export default function LoginScreen() {
  const { t } = useI18n();
  const { c } = useTheme();
  const [step, setStep] = useState<'email' | 'login' | 'register'>('email');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [birthDate, setBirthDate] = useState('1990-01-01');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const run = async (fn: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try { await fn(); } catch (e) { setError(e); } finally { setBusy(false); }
  };
  const done = () => (router.canGoBack() ? router.back() : router.replace('/'));
  const eye = (
    <IconButton icon={showPassword ? 'eye-off-outline' : 'eye-outline'} label={showPassword ? t('login.hidePassword') : t('login.showPassword')} onPress={() => setShowPassword((s) => !s)} color={c.textMuted} />
  );

  return (
    <Screen>
      <T v="title">{step === 'register' ? t('login.finish') : t('login.title')}</T>
      <Field label={t('login.email')} value={email} onChangeText={setEmail} autoCapitalize="none" keyboardType="email-address" autoComplete="email" textContentType="emailAddress" editable={step === 'email'} />
      {step === 'login' ? <Field label={t('login.password')} value={password} onChangeText={setPassword} secureTextEntry={!showPassword} autoComplete="current-password" textContentType="password" trailing={eye} /> : null}
      {step === 'register' ? (
        <>
          <Field label={t('login.firstName')} value={firstName} onChangeText={setFirstName} autoComplete="given-name" />
          <Field label={t('login.lastName')} value={lastName} onChangeText={setLastName} autoComplete="family-name" />
          <Field label={t('login.birthDate')} value={birthDate} onChangeText={setBirthDate} keyboardType="numbers-and-punctuation" />
          <Field label={t('login.password')} hint={t('login.passwordHint')} value={password} onChangeText={setPassword} secureTextEntry={!showPassword} autoComplete="new-password" textContentType="newPassword" trailing={eye} />
        </>
      ) : null}
      {error ? <ErrorBox error={error} /> : null}
      <Button
        title={step === 'email' ? t('common.continue') : step === 'login' ? t('login.logIn') : t('login.agree')}
        loading={busy}
        disabled={!email.includes('@')}
        onPress={() => run(async () => {
          if (step === 'email') setStep((await lookupEmail(email.trim())).exists ? 'login' : 'register');
          else if (step === 'login') { await login(email.trim(), password); done(); }
          else { await register({ firstName, lastName, email: email.trim(), password, birthDate }); done(); }
        })}
      />
      {step !== 'email' ? <Button title={t('login.otherEmail')} kind="ghost" onPress={() => { setStep('email'); setPassword(''); setError(null); }} /> : null}
      <Ornament />
      <Card tone="soft">
        <T v="label">{t('login.demo')}</T>
        <T v="small">{t('login.demoCustomer')}</T>
        <T v="small">{t('login.demoPharmacist')}</T>
        <T v="small">{t('login.demoAdmin')}</T>
      </Card>
    </Screen>
  );
}
