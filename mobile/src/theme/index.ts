import { useMemo } from 'react';
import { useColorScheme, type TextStyle } from 'react-native';

import { usePrefs } from '@/core/prefs';
import { fontFamilies, palettes, radius, spacing, textScales, typeScale, type SchemeName, type TextVariant } from './tokens';

export * from './tokens';

/**
 * Resolved theme for the current scheme, language and text size. Components read everything from here,
 * so a scheme or font-size change re-renders consistently and no screen hard-codes a colour.
 */
export function useTheme() {
  const system = useColorScheme();
  const { scheme: pref, language, textScale } = usePrefs();
  const scheme: SchemeName = pref === 'system' ? (system === 'dark' ? 'dark' : 'light') : pref;

  return useMemo(() => {
    const c = palettes[scheme];
    const scale = textScales[textScale];
    const display = fontFamilies.display[language];
    const isDisplay = (v: TextVariant) => v === 'display' || v === 'title' || v === 'h2';

    /** Typography for a variant. Arabic needs a little more line height for its ascenders/descenders. */
    const type = (variant: TextVariant, weight: 'regular' | 'medium' | 'semibold' | 'bold' = 'regular'): TextStyle => {
      const t = typeScale[variant];
      const family = isDisplay(variant) ? (variant === 'h2' ? fontFamilies.displayMedium[language] : display)
        : weight === 'bold' ? fontFamilies.bodyBold : weight === 'semibold' ? fontFamilies.bodySemibold : weight === 'medium' ? fontFamilies.bodyMedium : fontFamilies.body;
      return {
        fontFamily: family,
        fontSize: Math.round(t.size * scale * 10) / 10,
        lineHeight: Math.round(t.line * scale * (language === 'ar' ? 1.12 : 1)),
        color: c.text,
        textAlign: 'auto',
      };
    };

    const elevation = {
      shadowColor: c.shadow,
      shadowOpacity: scheme === 'dark' ? 0.35 : 0.08,
      shadowRadius: 10,
      shadowOffset: { width: 0, height: 3 },
      elevation: 2,
    };

    return { scheme, c, spacing, radius, type, scale, elevation, isDark: scheme === 'dark' };
  }, [scheme, language, textScale]);
}

export type Theme = ReturnType<typeof useTheme>;
