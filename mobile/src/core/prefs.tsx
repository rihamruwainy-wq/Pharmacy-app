import AsyncStorage from '@react-native-async-storage/async-storage';
import { getLocales } from 'expo-localization';
import * as Updates from 'expo-updates';
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { Alert, I18nManager, Platform } from 'react-native';

import type { TextScaleName } from '@/theme/tokens';

export type Language = 'en' | 'ar';
export type SchemePreference = 'system' | 'light' | 'dark';

export interface Preferences {
  language: Language;
  scheme: SchemePreference;
  textScale: TextScaleName;
  onboarded: boolean;
}

interface PrefsApi extends Preferences {
  ready: boolean;
  setLanguage: (language: Language) => Promise<void>;
  setScheme: (scheme: SchemePreference) => void;
  setTextScale: (scale: TextScaleName) => void;
  setOnboarded: (done: boolean) => void;
}

const KEY = 'medora.prefs.v1';
const deviceLanguage = (): Language => (getLocales()[0]?.languageCode === 'ar' ? 'ar' : 'en');
const defaults = (): Preferences => ({ language: deviceLanguage(), scheme: 'system', textScale: 'default', onboarded: false });

/** Web: direction is a document attribute; flex rows and text follow it without a reload. */
function applyWebDirection(language: Language) {
  if (Platform.OS !== 'web' || typeof document === 'undefined') return;
  document.documentElement.lang = language;
  document.documentElement.dir = language === 'ar' ? 'rtl' : 'ltr';
  // The app scrolls inside its own views. If the document itself can scroll, RTL moves its scrollbar to
  // the left edge and shifts the whole layout sideways, so the root element must never scroll.
  document.documentElement.style.overflow = 'hidden';
}

/**
 * Native: React Native fixes layout direction at start-up, so switching between English (LTR) and
 * Arabic (RTL) needs one reload. We persist first, then reload; if a reload isn't possible the user is
 * told to restart the app.
 */
async function applyNativeDirection(language: Language, reloadIfChanged: boolean) {
  if (Platform.OS === 'web') return;
  const rtl = language === 'ar';
  I18nManager.allowRTL(rtl);
  if (I18nManager.isRTL === rtl) return;
  I18nManager.forceRTL(rtl);
  if (!reloadIfChanged) return;
  try {
    await Updates.reloadAsync();
  } catch {
    Alert.alert(rtl ? 'أعد تشغيل التطبيق' : 'Restart required', rtl ? 'أغلق التطبيق وافتحه مرة أخرى لتطبيق اتجاه الكتابة.' : 'Close and reopen the app to finish switching language.');
  }
}

const PrefsContext = createContext<PrefsApi | null>(null);

export function PreferencesProvider({ children }: { children: ReactNode }) {
  const [prefs, setPrefs] = useState<Preferences>(defaults);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    AsyncStorage.getItem(KEY)
      .then((raw) => {
        const loaded = { ...defaults(), ...(raw ? (JSON.parse(raw) as Partial<Preferences>) : {}) };
        setPrefs(loaded);
        applyWebDirection(loaded.language);
        return applyNativeDirection(loaded.language, true);
      })
      .catch(() => undefined)
      .finally(() => setReady(true));
  }, []);

  const persist = useCallback((next: Preferences) => {
    setPrefs(next);
    AsyncStorage.setItem(KEY, JSON.stringify(next)).catch(() => undefined);
  }, []);

  const api = useMemo<PrefsApi>(() => ({
    ...prefs,
    ready,
    setLanguage: async (language) => {
      const next = { ...prefs, language };
      setPrefs(next);
      await AsyncStorage.setItem(KEY, JSON.stringify(next)).catch(() => undefined);
      applyWebDirection(language);
      await applyNativeDirection(language, true);
    },
    setScheme: (scheme) => persist({ ...prefs, scheme }),
    setTextScale: (textScale) => persist({ ...prefs, textScale }),
    setOnboarded: (onboarded) => persist({ ...prefs, onboarded }),
  }), [prefs, ready, persist]);

  return <PrefsContext.Provider value={api}>{children}</PrefsContext.Provider>;
}

export function usePrefs() {
  const ctx = useContext(PrefsContext);
  if (!ctx) throw new Error('usePrefs must be used inside PreferencesProvider');
  return ctx;
}
