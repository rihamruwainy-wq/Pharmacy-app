/**
 * Design tokens: the ONLY place colours, type, spacing and shape are defined.
 * Direction: "vintage apothecary, modern finish" - parchment and ink, apothecary green, brass and
 * oxblood accents, serif display type; but modern spacing, rounded surfaces and soft elevation.
 * Every text/background pair below meets WCAG AA (4.5:1) for body text in both schemes.
 * To re-skin the app for a client, edit this file only.
 */
export type SchemeName = 'light' | 'dark';

export interface Palette {
  background: string;    // page (parchment)
  surface: string;       // cards, sheets
  surfaceAlt: string;    // inputs, subtle fills
  border: string;
  ornament: string;      // decorative rules
  text: string;          // ink
  textMuted: string;
  primary: string;       // apothecary green: main actions
  onPrimary: string;
  primarySoft: string;
  accent: string;        // brass: highlights, prices
  danger: string;        // oxblood
  dangerSoft: string;
  warn: string;
  warnSoft: string;
  info: string;
  infoSoft: string;
  success: string;
  successSoft: string;
  shadow: string;
  overlay: string;
}

/**
 * Palette v2, "heritage apothecary": cream ground, ink text, bottle green, antique gold - the classic
 * combination of 18th/19th-century apothecary houses (e.g. D.R. Harris, Granado), with green as the
 * universal pharmacy signal. v1 leaned on browns (beige paper, brown ink, brown dark mode), which read dull;
 * v2 keeps the warmth in the cream and gold only. Every text pair is checked for WCAG AA (see README).
 */
export const palettes: Record<SchemeName, Palette> = {
  light: {
    background: '#F7F3EA',  // cream
    surface: '#FFFEFA',
    surfaceAlt: '#EFE9DC',
    border: '#E0D7C5',
    ornament: '#B8975A',    // antique gold rules and emblems
    text: '#1C2422',        // ink with a green undertone (14.3:1 on cream)
    textMuted: '#55605B',   // 5.9:1
    primary: '#1F5A47',     // bottle green (white text 8.0:1)
    onPrimary: '#FFFEFA',
    primarySoft: '#E3EEE8',
    accent: '#8A6421',      // antique gold for prices and highlights (4.8:1 on cream)
    danger: '#9A2B2B',      // oxblood
    dangerSoft: '#F7E4E1',
    warn: '#8A5300',
    warnSoft: '#FAEEDB',
    info: '#2B587A',
    infoSoft: '#E4EEF5',
    success: '#1F5A47',
    successSoft: '#E3EEE8',
    shadow: '#1C2422',
    overlay: 'rgba(28,36,34,0.45)',
  },
  dark: {
    background: '#0E1513',  // green-black (apothecary bottle glass), not brown
    surface: '#16201C',
    surfaceAlt: '#1E2A25',
    border: '#2C3A34',
    ornament: '#8C7346',
    text: '#EEF2EC',        // 16.3:1
    textMuted: '#A9B6AF',   // 8.8:1
    primary: '#7DC4A6',     // mint
    onPrimary: '#0B1F17',
    primarySoft: '#1C3A2F',
    accent: '#D8B56E',      // gold
    danger: '#F09A8F',
    dangerSoft: '#3A1F1D',
    warn: '#E9BD6A',
    warnSoft: '#33280F',
    info: '#9CC3E0',
    infoSoft: '#1A2A36',
    success: '#7DC4A6',
    successSoft: '#1C3A2F',
    shadow: '#000000',
    overlay: 'rgba(0,0,0,0.6)',
  },
};

export const spacing = { xxs: 2, xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32 } as const;
export const radius = { sm: 8, md: 12, lg: 18, pill: 999 } as const;

/** Minimum touch target (WCAG 2.5.5 / platform guidelines): 48 dp. */
export const touchTarget = 48;

/** Font family names registered in the root layout (expo-google-fonts). */
export const fontFamilies = {
  display: { en: 'PlayfairDisplay_700Bold', ar: 'Amiri_700Bold' },
  displayMedium: { en: 'PlayfairDisplay_600SemiBold', ar: 'Amiri_700Bold' },
  body: 'IBMPlexSansArabic_400Regular',
  bodyMedium: 'IBMPlexSansArabic_500Medium',
  bodySemibold: 'IBMPlexSansArabic_600SemiBold',
  bodyBold: 'IBMPlexSansArabic_700Bold',
} as const;

/** Base sizes in dp at text scale 1.0 (body 16 for readability; users can scale up in Settings). */
export const typeScale = {
  display: { size: 30, line: 38 },
  title: { size: 24, line: 31 },
  h2: { size: 18.5, line: 25 },
  body: { size: 16, line: 24 },
  label: { size: 15, line: 21 },
  small: { size: 13.5, line: 19 },
  caption: { size: 12.5, line: 17 },
} as const;

export type TextVariant = keyof typeof typeScale;

/** User-selectable text size multipliers (on top of the OS font scale). */
export const textScales = { default: 1, large: 1.15, xlarge: 1.3 } as const;
export type TextScaleName = keyof typeof textScales;
