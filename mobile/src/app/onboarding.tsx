import { router } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { usePrefs, type Language } from '@/core/prefs';
import { useI18n, type Key } from '@/i18n';
import { fontFamilies, useTheme } from '@/theme';
import { Button, Icon, Ornament, Row, T, type IconName } from '@/ui/kit';

const SLIDES: { icon: IconName; title: Key; body: Key }[] = [
  { icon: 'storefront-outline', title: 'onb.s1.title', body: 'onb.s1.body' },
  { icon: 'bag-check-outline', title: 'onb.s2.title', body: 'onb.s2.body' },
  { icon: 'shield-checkmark-outline', title: 'onb.s3.title', body: 'onb.s3.body' },
  { icon: 'calendar-outline', title: 'onb.s4.title', body: 'onb.s4.body' },
];

/**
 * First-run introduction. Step 0 is the language choice (in both languages, so anyone can read it);
 * then four short steps. Everything is skippable, nothing animates on its own, and the step is announced.
 */
export default function Onboarding() {
  const prefs = usePrefs();
  const { t } = useI18n();
  const { c, spacing, radius } = useTheme();
  const [step, setStep] = useState(0);
  const total = SLIDES.length;

  const finish = (to: '/' | '/login') => {
    prefs.setOnboarded(true);
    router.replace('/');
    if (to === '/login') setTimeout(() => router.push('/login'), 0);
  };

  if (step === 0) {
    const choose = async (language: Language) => {
      setStep(1);
      if (language !== prefs.language) await prefs.setLanguage(language); // may reload the app on phones (direction change)
    };
    return (
      <SafeAreaView style={[styles.page, { backgroundColor: c.background }]}>
        <View style={[styles.inner, { gap: spacing.lg }]}>
          <Emblem icon="leaf-outline" />
          <T v="display" center>Medora · ميدورا</T>
          <Ornament />
          <T v="title" center>Choose your language</T>
          <T v="title" center style={{ fontFamily: fontFamilies.display.ar }}>اختر لغتك</T>
          <View style={{ gap: spacing.md, marginTop: spacing.md }}>
            <LanguageButton label="English" sub="Continue in English" onPress={() => choose('en')} selected={prefs.language === 'en'} lang="en" />
            <LanguageButton label="العربية" sub="المتابعة بالعربية" onPress={() => choose('ar')} selected={prefs.language === 'ar'} lang="ar" />
          </View>
          <T v="small" tone="muted" center>{t('onb.language.body')}</T>
        </View>
      </SafeAreaView>
    );
  }

  const slide = SLIDES[step - 1]!;
  const last = step === total;
  return (
    <SafeAreaView style={[styles.page, { backgroundColor: c.background }]}>
      <Row style={{ justifyContent: 'space-between', paddingHorizontal: spacing.lg }}>
        <Button title={t('common.back')} kind="ghost" icon="chevron-back" compact onPress={() => setStep(step - 1)} style={{ borderWidth: 0 }} />
        {!last ? <Button title={t('common.skip')} kind="ghost" compact onPress={() => finish('/')} style={{ borderWidth: 0 }} /> : null}
      </Row>
      <View style={[styles.inner, { gap: spacing.lg }]}>
        <Emblem icon={slide.icon} />
        <T v="display" center>{t(slide.title)}</T>
        <Ornament />
        <T v="body" tone="muted" center style={{ fontSize: 17, lineHeight: 26 }}>{t(slide.body)}</T>
      </View>
      <View style={{ paddingHorizontal: spacing.xl, paddingBottom: spacing.xl, gap: spacing.md }}>
        <View accessible accessibilityRole="progressbar" accessibilityLabel={t('onb.step', { current: step, total })} accessibilityValue={{ min: 1, max: total, now: step }} style={styles.dots}>
          {SLIDES.map((s, i) => (
            <View key={s.title} style={{ width: i + 1 === step ? 22 : 8, height: 8, borderRadius: radius.pill, backgroundColor: i + 1 === step ? c.primary : c.border }} />
          ))}
        </View>
        {last ? (
          <>
            <Button title={t('onb.start')} icon="arrow-forward" onPress={() => finish('/')} />
            <Button title={t('onb.staff')} kind="ghost" onPress={() => finish('/login')} />
          </>
        ) : (
          <Button title={t('common.next')} onPress={() => setStep(step + 1)} />
        )}
      </View>
    </SafeAreaView>
  );
}

/** Vintage-label emblem: double ring around an icon. Decorative. */
function Emblem({ icon }: { icon: IconName }) {
  const { c } = useTheme();
  return (
    <View accessible={false} style={[styles.emblemOuter, { borderColor: c.ornament }]}>
      <View style={[styles.emblemInner, { backgroundColor: c.primarySoft, borderColor: c.primary }]}>
        <Icon name={icon} size={52} color={c.primary} />
      </View>
    </View>
  );
}

/** Each language option is set in its own script's typeface, whatever the current app language. */
function LanguageButton({ label, sub, onPress, selected, lang }: { label: string; sub: string; onPress: () => void; selected: boolean; lang: Language }) {
  const { c, radius, spacing } = useTheme();
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`${label}. ${sub}`}
      accessibilityState={{ selected }}
      onPress={onPress}
      style={({ pressed }) => [{ borderWidth: 1.5, borderColor: selected ? c.primary : c.border, backgroundColor: selected ? c.primarySoft : c.surface, borderRadius: radius.lg, padding: spacing.lg, minHeight: 72, justifyContent: 'center' }, pressed && { opacity: 0.85 }]}>
      <T v="title" center style={{ fontFamily: fontFamilies.display[lang] }}>{label}</T>
      <T v="small" tone="muted" center>{sub}</T>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  page: { flex: 1 },
  inner: { flex: 1, justifyContent: 'center', paddingHorizontal: 28, maxWidth: 560, width: '100%', alignSelf: 'center' },
  dots: { flexDirection: 'row', gap: 6, justifyContent: 'center', paddingVertical: 8 },
  emblemOuter: { alignSelf: 'center', width: 150, height: 150, borderRadius: 75, borderWidth: 1, alignItems: 'center', justifyContent: 'center' },
  emblemInner: { width: 124, height: 124, borderRadius: 62, borderWidth: 2, alignItems: 'center', justifyContent: 'center' },
});
