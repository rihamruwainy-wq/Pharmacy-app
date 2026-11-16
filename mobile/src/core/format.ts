/** Short, human-friendly order reference (last 6 characters of the opaque id). */
export const shortId = (id: string) => id.slice(-6).toUpperCase();

// Locale-aware money, dates and status labels live in '@/i18n' (useI18n), so they follow the chosen language.
