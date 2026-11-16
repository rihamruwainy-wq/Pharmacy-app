import { useMemo } from 'react';

import { ApiError } from '@/core/api/http';
import { usePrefs, type Language } from '@/core/prefs';
import { ar } from './ar';
import { en, type TranslationKey } from './en';

type PluralBase<K> = K extends `${infer B}_other` ? B : never;
/** Any real key, or the base of a plural family (`search.results` -> `_one` / `_other` / ...). */
export type Key = TranslationKey | PluralBase<TranslationKey>;
type Params = Record<string, string | number>;

const dictionaries: Record<Language, Record<string, string>> = { en, ar };

/**
 * Number system for Arabic: Western digits (0-9) are the norm in Gulf/Levant retail apps and read
 * best next to prices; switch to 'ar' (Arabic-Indic digits ٠-٩) here if a client prefers them.
 */
const locales: Record<Language, string> = { en: 'en-US', ar: 'ar-u-nu-latn' };

export function translate(language: Language, key: Key, params?: Params): string {
  const dict = dictionaries[language];
  let template = dict[key];
  if (template === undefined && params && typeof params.count === 'number') {
    const category = new Intl.PluralRules(locales[language]).select(params.count);
    template = (params.count === 0 ? dict[`${key}_zero`] : undefined) ?? dict[`${key}_${category}`] ?? dict[`${key}_other`];
  }
  template ??= (en as Record<string, string>)[key] ?? (en as Record<string, string>)[`${key}_other`] ?? key; // never show a blank
  if (!params) return template;
  return template.replace(/\{(\w+)\}/g, (_, name: string) => String(params[name] ?? `{${name}}`));
}

/** Translation + locale-aware formatting for the current language. */
export function useI18n() {
  const { language } = usePrefs();
  return useMemo(() => {
    const locale = locales[language];
    const t = (key: Key, params?: Params) => translate(language, key, params);
    return {
      language,
      isRTL: language === 'ar',
      t,
      money: (minor: number, currency = 'USD') => new Intl.NumberFormat(locale, { style: 'currency', currency }).format(minor / 100),
      number: (n: number) => new Intl.NumberFormat(locale).format(n),
      date: (iso: string) => new Date(iso).toLocaleDateString(locale, { day: 'numeric', month: 'short', year: 'numeric' }),
      dateTime: (iso: string) => new Date(iso).toLocaleString(locale, { weekday: 'short', day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' }),
      time: (iso: string) => new Date(iso).toLocaleTimeString(locale, { hour: 'numeric', minute: '2-digit' }),
      dayLabel: (iso: string) => new Date(iso).toLocaleDateString(locale, { weekday: 'long', day: 'numeric', month: 'long' }),
      /** Category labels are translated on the device (the API sends stable ids). */
      category: (id: string) => translate(language, `cat.${id}` as Key),
      status: (s: string) => translate(language, `status.${s}` as Key),
      /**
       * User-facing error text. English uses the server's (more specific) message; Arabic uses the
       * translated message for the error code, so users never see an English sentence in Arabic mode.
       */
      error: (e: unknown) => {
        if (!(e instanceof ApiError)) return t('error.generic');
        const key = `error.${e.code}` as Key;
        const known = key in en;
        if (language === 'en' && e.status !== 0) return e.message || (known ? t(key) : t('error.generic'));
        return known ? t(key) : t('error.generic');
      },
    };
  }, [language]);
}

export type I18n = ReturnType<typeof useI18n>;
