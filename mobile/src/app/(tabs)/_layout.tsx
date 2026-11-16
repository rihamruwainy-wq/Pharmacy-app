import Ionicons from '@expo/vector-icons/Ionicons';
import { Redirect, Tabs } from 'expo-router';
import type { ComponentProps } from 'react';
import type { ColorValue } from 'react-native';

import { useSession } from '@/core/auth/session';
import { usePrefs } from '@/core/prefs';
import { useCart } from '@/features/cart';
import { useI18n } from '@/i18n';
import { fontFamilies, useTheme } from '@/theme';

type IconName = ComponentProps<typeof Ionicons>['name'];

/** Filled icon when the tab is selected, outline otherwise (state is not shown by colour alone). */
const icon = (name: string) => {
  const TabIcon = ({ color, focused }: { color: ColorValue; focused: boolean }) =>
    <Ionicons name={(focused ? name : `${name}-outline`) as IconName} size={24} color={color as string} />;
  TabIcon.displayName = `TabIcon(${name})`;
  return TabIcon;
};

/** Role-based navigation: customers shop; pharmacists work the queue; admins also see the back office. */
export default function TabsLayout() {
  const { onboarded } = usePrefs();
  const { isStaff, isAdmin } = useSession();
  const { count } = useCart();
  const { c } = useTheme();
  const { t, language } = useI18n();

  if (!onboarded) return <Redirect href="/onboarding" />;
  const customer = !isStaff;

  return (
    <Tabs
      screenOptions={{
        tabBarActiveTintColor: c.primary,
        tabBarInactiveTintColor: c.textMuted,
        tabBarStyle: { backgroundColor: c.surface, borderTopColor: c.border, minHeight: 60 },
        tabBarLabelStyle: { fontFamily: fontFamilies.bodySemibold, fontSize: 12 },
        headerStyle: { backgroundColor: c.surface },
        headerShadowVisible: false,
        headerTitleStyle: { fontFamily: fontFamilies.display[language], color: c.text, fontSize: 22 },
        sceneStyle: { backgroundColor: c.background },
      }}>
      <Tabs.Screen name="index" options={{ title: t('app.name'), tabBarLabel: t('tabs.shop'), tabBarIcon: icon('storefront'), href: customer ? undefined : null, tabBarBadge: count > 0 ? count : undefined, tabBarBadgeStyle: { backgroundColor: c.accent } }} />
      <Tabs.Screen name="care" options={{ title: t('care.title'), tabBarLabel: t('tabs.care'), tabBarIcon: icon('medkit'), href: customer ? undefined : null }} />
      <Tabs.Screen name="saved" options={{ title: t('saved.title'), tabBarLabel: t('tabs.saved'), tabBarIcon: icon('heart'), href: customer ? undefined : null }} />
      <Tabs.Screen name="orders" options={{ title: t('orders.title'), tabBarLabel: t('tabs.orders'), tabBarIcon: icon('receipt'), href: customer ? undefined : null }} />
      <Tabs.Screen name="queue" options={{ title: t('tabs.queue'), tabBarIcon: icon('clipboard'), href: isStaff ? undefined : null }} />
      <Tabs.Screen name="copilot" options={{ title: t('copilot.title'), tabBarLabel: t('tabs.copilot'), tabBarIcon: icon('sparkles'), href: isStaff ? undefined : null }} />
      <Tabs.Screen name="dashboard" options={{ title: t('tabs.dashboard'), tabBarIcon: icon('stats-chart'), href: isAdmin ? undefined : null }} />
      <Tabs.Screen name="assistant" options={{ title: t('assistant.title'), tabBarLabel: t('tabs.assistant'), tabBarIcon: icon('chatbubbles'), href: isAdmin ? undefined : null }} />
      <Tabs.Screen name="account" options={{ title: t('tabs.account'), tabBarIcon: icon('person-circle') }} />
    </Tabs>
  );
}
