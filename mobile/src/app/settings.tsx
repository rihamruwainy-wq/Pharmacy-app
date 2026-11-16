import Constants from 'expo-constants';
import { router } from 'expo-router';
import { Platform } from 'react-native';

import { usePrefs, type Language, type SchemePreference } from '@/core/prefs';
import { useI18n } from '@/i18n';
import type { TextScaleName } from '@/theme';
import { Button, Card, Ornament, Screen, Segmented, T } from '@/ui/kit';

/** Language, appearance and text size: the three settings the average user most needs, with a live preview. */
export default function SettingsScreen() {
  const prefs = usePrefs();
  const { t } = useI18n();

  return (
    <Screen>
      <Card style={{ gap: 12 }}>
        <T v="h2">{t('settings.language')}</T>
        <Segmented<Language>
          label={t('settings.language')}
          value={prefs.language}
          onChange={(l) => prefs.setLanguage(l)}
          options={[{ value: 'en', label: 'English' }, { value: 'ar', label: 'العربية' }]}
        />
        {Platform.OS !== 'web' ? <T v="small" tone="muted">{t('settings.languageNote')}</T> : null}
      </Card>

      <Card style={{ gap: 12 }}>
        <T v="h2">{t('settings.appearance')}</T>
        <Segmented<SchemePreference>
          label={t('settings.appearance')}
          value={prefs.scheme}
          onChange={prefs.setScheme}
          options={[
            { value: 'system', label: t('settings.system'), icon: 'phone-portrait-outline' },
            { value: 'light', label: t('settings.light'), icon: 'sunny-outline' },
            { value: 'dark', label: t('settings.dark'), icon: 'moon-outline' },
          ]}
        />
      </Card>

      <Card style={{ gap: 12 }}>
        <T v="h2">{t('settings.textSize')}</T>
        <Segmented<TextScaleName>
          label={t('settings.textSize')}
          value={prefs.textScale}
          onChange={prefs.setTextScale}
          options={[
            { value: 'default', label: t('settings.text.default') },
            { value: 'large', label: t('settings.text.large') },
            { value: 'xlarge', label: t('settings.text.xlarge') },
          ]}
        />
        <Ornament />
        <T>{t('settings.preview')}</T>
      </Card>

      <Button title={t('settings.intro')} kind="ghost" icon="book-outline" onPress={() => { prefs.setOnboarded(false); router.replace('/onboarding'); }} />
      <T v="small" tone="muted" center>{t('app.name')} · {t('settings.version', { version: Constants.expoConfig?.version ?? '1.0.0' })}</T>
    </Screen>
  );
}
