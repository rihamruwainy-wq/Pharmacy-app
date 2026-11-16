import { Amiri_700Bold } from '@expo-google-fonts/amiri';
import {
  IBMPlexSansArabic_400Regular, IBMPlexSansArabic_500Medium, IBMPlexSansArabic_600SemiBold, IBMPlexSansArabic_700Bold,
} from '@expo-google-fonts/ibm-plex-sans-arabic';
import { PlayfairDisplay_600SemiBold, PlayfairDisplay_700Bold } from '@expo-google-fonts/playfair-display';
import Ionicons from '@expo/vector-icons/Ionicons';
import { PersistQueryClientProvider } from '@tanstack/react-query-persist-client';
import { useFonts } from 'expo-font';
import { DarkTheme, DefaultTheme, Stack, ThemeProvider } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect, useMemo } from 'react';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { loadSession, useSession } from '@/core/auth/session';
import { PreferencesProvider, usePrefs } from '@/core/prefs';
import { persistOptions, queryClient } from '@/core/query';
import { CartProvider } from '@/features/cart';
import { useI18n } from '@/i18n';
import { fontFamilies, useTheme } from '@/theme';

SplashScreen.preventAutoHideAsync().catch(() => undefined);

export default function RootLayout() {
  return (
    <PreferencesProvider>
      <App />
    </PreferencesProvider>
  );
}

function App() {
  const [fontsLoaded] = useFonts({
    PlayfairDisplay_600SemiBold, PlayfairDisplay_700Bold, Amiri_700Bold,
    IBMPlexSansArabic_400Regular, IBMPlexSansArabic_500Medium, IBMPlexSansArabic_600SemiBold, IBMPlexSansArabic_700Bold,
    ...Ionicons.font,
  });
  const prefs = usePrefs();
  const { ready: sessionReady } = useSession();
  const { c, isDark } = useTheme();
  const { t, language } = useI18n();
  const ready = fontsLoaded && prefs.ready && sessionReady;

  useEffect(() => { loadSession(); }, []);
  useEffect(() => { if (ready) SplashScreen.hideAsync().catch(() => undefined); }, [ready]);

  // Navigation chrome (headers, tab bar, transitions) follows the same tokens as the content.
  const navTheme = useMemo(() => {
    const base = isDark ? DarkTheme : DefaultTheme;
    return { ...base, colors: { ...base.colors, primary: c.primary, background: c.background, card: c.surface, text: c.text, border: c.border, notification: c.danger } };
  }, [isDark, c]);

  if (!ready) return null; // splash stays up: no flash of unstyled text or of the signed-out UI

  const headerTitleStyle = { fontFamily: fontFamilies.displayMedium[language], color: c.text, fontSize: 19 };
  return (
    <SafeAreaProvider>
      {/* The persisted cache (public catalogue only) is restored before queries run: instant, offline-capable cold start. */}
      <PersistQueryClientProvider client={queryClient} persistOptions={persistOptions}>
        <CartProvider>
          <ThemeProvider value={navTheme}>
            <StatusBar style={isDark ? 'light' : 'dark'} />
            <Stack screenOptions={{ headerStyle: { backgroundColor: c.surface }, headerTintColor: c.primary, headerTitleStyle, headerShadowVisible: false, contentStyle: { backgroundColor: c.background }, headerBackButtonDisplayMode: 'minimal' }}>
              <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
              <Stack.Screen name="onboarding" options={{ headerShown: false, gestureEnabled: false, animation: 'fade' }} />
              <Stack.Screen name="search" options={{ title: t('search.title'), animation: 'fade_from_bottom' }} />
              <Stack.Screen name="product/[id]" options={{ title: '' }} />
              <Stack.Screen name="cart" options={{ title: t('cart.title') }} />
              <Stack.Screen name="checkout/[id]" options={{ title: t('checkout.title') }} />
              <Stack.Screen name="order/[id]" options={{ title: '' }} />
              <Stack.Screen name="service/[id]" options={{ title: '' }} />
              <Stack.Screen name="settings" options={{ title: t('settings.title') }} />
              <Stack.Screen name="rx/new" options={{ title: t('rx.title'), presentation: 'modal' }} />
              <Stack.Screen name="login" options={{ title: t('account.loginSignup'), presentation: 'modal' }} />
              <Stack.Screen name="verify/[id]" options={{ title: t('staff.verifyTitle') }} />
            </Stack>
          </ThemeProvider>
        </CartProvider>
      </PersistQueryClientProvider>
    </SafeAreaProvider>
  );
}
