import Ionicons from '@expo/vector-icons/Ionicons';
import { Image } from 'expo-image';
import { memo, useEffect, useState, type ComponentProps, type ReactNode } from 'react';
import {
  AccessibilityInfo, ActivityIndicator, Pressable, ScrollView, StyleSheet, Text, TextInput, View,
  type RefreshControlProps, type StyleProp, type TextInputProps, type TextStyle, type ViewStyle,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import type { InteractionAlert, ProductSummary } from '@/core/api/schemas';
import { useI18n } from '@/i18n';
import { touchTarget, useTheme, type TextVariant } from '@/theme';

// =====================================================================================================
// Primitives. Screens compose these; they never hard-code colours, fonts or sizes.
// =====================================================================================================

export type IconName = ComponentProps<typeof Ionicons>['name'];

/** Directional icons (arrows, chevrons) are mirrored in right-to-left languages. */
export function Icon({ name, size = 22, color, directional }: { name: IconName; size?: number; color?: string; directional?: boolean }) {
  const { c } = useTheme();
  const { isRTL } = useI18n();
  return (
    <View style={directional && isRTL ? { transform: [{ scaleX: -1 }] } : undefined} accessible={false} importantForAccessibility="no-hide-descendants">
      <Ionicons name={name} size={size} color={color ?? c.text} />
    </View>
  );
}

export function Screen({ children, scroll = true, padded = true, refreshControl }: {
  children: ReactNode; scroll?: boolean; padded?: boolean; refreshControl?: React.ReactElement<RefreshControlProps>;
}) {
  const { c, spacing } = useTheme();
  const inner = <View style={[padded && { padding: spacing.lg, gap: spacing.md }, styles.content]}>{children}</View>;
  return (
    <SafeAreaView style={{ flex: 1, backgroundColor: c.background }} edges={['left', 'right']}>
      {scroll ? (
        <ScrollView keyboardShouldPersistTaps="handled" contentContainerStyle={{ paddingBottom: spacing.xxl * 2 }} refreshControl={refreshControl}>{inner}</ScrollView>
      ) : inner}
    </SafeAreaView>
  );
}

type Tone = 'default' | 'muted' | 'primary' | 'accent' | 'danger' | 'onPrimary';

/** Text. Display variants use the serif face and are announced as headings by screen readers. */
export function T({ v = 'body', weight, tone = 'default', style, children, numberOfLines, center }: {
  v?: TextVariant | 'muted'; weight?: 'regular' | 'medium' | 'semibold' | 'bold'; tone?: Tone; style?: StyleProp<TextStyle>;
  children: ReactNode; numberOfLines?: number; center?: boolean;
}) {
  const { type, c } = useTheme();
  const variant: TextVariant = v === 'muted' ? 'body' : v;
  const color = { default: c.text, muted: c.textMuted, primary: c.primary, accent: c.accent, danger: c.danger, onPrimary: c.onPrimary }[v === 'muted' ? 'muted' : tone];
  const heading = variant === 'display' || variant === 'title' || variant === 'h2';
  return (
    <Text
      accessibilityRole={heading ? 'header' : undefined}
      numberOfLines={numberOfLines}
      maxFontSizeMultiplier={2}
      style={[type(variant, weight ?? (variant === 'label' ? 'semibold' : 'regular')), { color }, center && { textAlign: 'center' }, style]}>
      {children}
    </Text>
  );
}

export function Button({ title, onPress, kind = 'primary', icon, disabled, loading, hint, style, compact }: {
  title: string; onPress: () => void; kind?: 'primary' | 'secondary' | 'danger' | 'ghost'; icon?: IconName;
  disabled?: boolean; loading?: boolean; hint?: string; style?: StyleProp<ViewStyle>; compact?: boolean;
}) {
  const { c, radius, type, spacing } = useTheme();
  const bg = { primary: c.primary, secondary: c.primarySoft, danger: c.danger, ghost: 'transparent' }[kind];
  const fg = kind === 'primary' ? c.onPrimary : kind === 'danger' ? c.surface : c.primary;
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={title}
      accessibilityHint={hint}
      accessibilityState={{ disabled: !!disabled || !!loading, busy: !!loading }}
      onPress={onPress}
      disabled={disabled || loading}
      style={({ pressed }) => [
        styles.button,
        { backgroundColor: bg, borderRadius: radius.md, minHeight: compact ? 40 : touchTarget, paddingHorizontal: compact ? spacing.md : spacing.lg, opacity: disabled ? 0.45 : pressed ? 0.85 : 1 },
        kind === 'ghost' && { borderWidth: 1, borderColor: c.border },
        style,
      ]}>
      {loading ? <ActivityIndicator color={fg} /> : (
        <>
          {icon ? <Icon name={icon} size={20} color={fg} directional={/arrow|chevron|send/.test(icon)} /> : null}
          <Text maxFontSizeMultiplier={1.6} style={[type('label', 'semibold'), { color: fg, textAlign: 'center' }]}>{title}</Text>
        </>
      )}
    </Pressable>
  );
}

/** Icon-only button: always has a spoken label and a 48 dp hit area. */
export function IconButton({ icon, label, onPress, color, directional, filled }: {
  icon: IconName; label: string; onPress: () => void; color?: string; directional?: boolean; filled?: boolean;
}) {
  const { c } = useTheme();
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      onPress={onPress}
      hitSlop={6}
      style={({ pressed }) => [styles.iconButton, filled && { backgroundColor: c.surface, borderColor: c.border, borderWidth: StyleSheet.hairlineWidth }, pressed && { opacity: 0.7 }]}>
      <Icon name={icon} color={color ?? c.text} directional={directional} />
    </Pressable>
  );
}

export function Card({ children, onPress, label, hint, style, tone = 'surface' }: {
  children: ReactNode; onPress?: () => void; label?: string; hint?: string; style?: StyleProp<ViewStyle>; tone?: 'surface' | 'soft' | 'danger' | 'warn';
}) {
  const { c, radius, spacing, elevation } = useTheme();
  const bg = { surface: c.surface, soft: c.primarySoft, danger: c.dangerSoft, warn: c.warnSoft }[tone];
  const base: StyleProp<ViewStyle> = [{ backgroundColor: bg, borderRadius: radius.lg, borderWidth: StyleSheet.hairlineWidth, borderColor: c.border, padding: spacing.lg, gap: spacing.sm }, tone === 'surface' && elevation, style];
  if (!onPress) return <View style={base}>{children}</View>;
  return (
    <Pressable accessibilityRole="button" accessibilityLabel={label} accessibilityHint={hint} onPress={onPress} style={({ pressed }) => [base, pressed && { opacity: 0.9, transform: [{ scale: 0.995 }] }]}>
      {children}
    </Pressable>
  );
}

/** Selectable pill (filters, options). Announces its selected state. */
export function Chip({ label, selected, onPress, icon }: { label: string; selected?: boolean; onPress: () => void; icon?: IconName }) {
  const { c, radius, type, spacing } = useTheme();
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ selected: !!selected }}
      accessibilityLabel={label}
      onPress={onPress}
      hitSlop={4}
      style={({ pressed }) => [
        styles.chip,
        { borderRadius: radius.pill, borderColor: selected ? c.primary : c.border, backgroundColor: selected ? c.primary : c.surface, paddingHorizontal: spacing.md },
        pressed && { opacity: 0.8 },
      ]}>
      {icon ? <Icon name={icon} size={16} color={selected ? c.onPrimary : c.textMuted} /> : null}
      {selected && !icon ? <Icon name="checkmark" size={16} color={c.onPrimary} /> : null}
      <Text maxFontSizeMultiplier={1.6} style={[type('small', 'semibold'), { color: selected ? c.onPrimary : c.text }]}>{label}</Text>
    </Pressable>
  );
}

export function Badge({ label, tone = 'info', icon }: { label: string; tone?: 'info' | 'warn' | 'danger' | 'ok' | 'neutral'; icon?: IconName }) {
  const { c, type, radius } = useTheme();
  const [bg, fg] = { info: [c.infoSoft, c.info], warn: [c.warnSoft, c.warn], danger: [c.dangerSoft, c.danger], ok: [c.successSoft, c.success], neutral: [c.surfaceAlt, c.textMuted] }[tone];
  return (
    <View style={[styles.badge, { backgroundColor: bg, borderRadius: radius.pill }]}>
      {icon ? <Icon name={icon} size={14} color={fg} /> : null}
      <Text maxFontSizeMultiplier={1.6} style={[type('caption', 'semibold'), { color: fg }]}>{label}</Text>
    </View>
  );
}

/** Labelled text input with optional hint and error; the label is what screen readers announce. */
export function Field({ label, hint, error, trailing, ...input }: TextInputProps & { label: string; hint?: string; error?: string; trailing?: ReactNode }) {
  const { c, type, radius, spacing } = useTheme();
  const { isRTL } = useI18n();
  return (
    <View style={{ gap: spacing.xs }}>
      <T v="label">{label}</T>
      <View style={[styles.inputWrap, { borderRadius: radius.md, borderColor: error ? c.danger : c.border, backgroundColor: c.surface }]}>
        <TextInput
          accessibilityLabel={label}
          accessibilityHint={hint}
          placeholderTextColor={c.textMuted}
          maxFontSizeMultiplier={1.8}
          style={[type('body'), styles.input, { textAlign: isRTL ? 'right' : 'left', writingDirection: isRTL ? 'rtl' : 'ltr' }]}
          {...input}
        />
        {trailing}
      </View>
      {error ? <Text accessibilityLiveRegion="polite" style={[type('small', 'medium'), { color: c.danger }]}>{error}</Text>
        : hint ? <T v="small" tone="muted">{hint}</T> : null}
    </View>
  );
}

export const Row = ({ children, style, wrap, gap }: { children: ReactNode; style?: StyleProp<ViewStyle>; wrap?: boolean; gap?: number }) => (
  <View style={[{ flexDirection: 'row', alignItems: 'center', gap: gap ?? 8 }, wrap && { flexWrap: 'wrap' }, style]}>{children}</View>
);

/** Vintage ornamental rule: thin line, small diamond, thin line. Purely decorative (hidden from screen readers). */
export function Ornament({ style }: { style?: StyleProp<ViewStyle> }) {
  const { c } = useTheme();
  return (
    <View accessible={false} importantForAccessibility="no-hide-descendants" style={[{ flexDirection: 'row', alignItems: 'center', gap: 8, marginVertical: 4 }, style]}>
      <View style={{ flex: 1, height: StyleSheet.hairlineWidth, backgroundColor: c.ornament }} />
      <View style={{ width: 7, height: 7, transform: [{ rotate: '45deg' }], borderWidth: 1, borderColor: c.ornament }} />
      <View style={{ flex: 1, height: StyleSheet.hairlineWidth, backgroundColor: c.ornament }} />
    </View>
  );
}

export function SectionHeader({ title, action, onAction }: { title: string; action?: string; onAction?: () => void }) {
  return (
    <Row style={{ justifyContent: 'space-between', marginTop: 8 }}>
      <T v="h2" style={{ flexShrink: 1 }}>{title}</T>
      {action && onAction ? (
        <Pressable accessibilityRole="button" onPress={onAction} hitSlop={10} style={{ minHeight: touchTarget, justifyContent: 'center' }}>
          <T v="label" tone="primary">{action}</T>
        </Pressable>
      ) : null}
    </Row>
  );
}

export function MoneyRow({ label, minor, bold }: { label: string; minor: number; bold?: boolean }) {
  const { money } = useI18n();
  return (
    <Row style={{ justifyContent: 'space-between' }}>
      <T v={bold ? 'h2' : 'body'} tone={bold ? 'default' : 'muted'}>{label}</T>
      <T v={bold ? 'h2' : 'body'} weight={bold ? 'bold' : 'medium'}>{money(minor)}</T>
    </Row>
  );
}

export function Loading({ label }: { label?: string }) {
  const { c } = useTheme();
  const { t } = useI18n();
  return (
    <View accessible accessibilityRole="progressbar" accessibilityLabel={label ?? t('common.loading')} style={{ padding: 32, alignItems: 'center', gap: 8 }}>
      <ActivityIndicator color={c.primary} size="large" />
      {label ? <T v="small" tone="muted">{label}</T> : null}
    </View>
  );
}

export function Empty({ icon = 'leaf-outline', title, body, action, onAction }: { icon?: IconName; title: string; body?: string; action?: string; onAction?: () => void }) {
  const { c, spacing } = useTheme();
  return (
    <View style={{ padding: spacing.xl, alignItems: 'center', gap: spacing.sm }}>
      <View style={[styles.emptyIcon, { backgroundColor: c.primarySoft, borderColor: c.ornament }]}><Icon name={icon} size={30} color={c.primary} /></View>
      <T v="h2" center>{title}</T>
      {body ? <T v="muted" center>{body}</T> : null}
      {action && onAction ? <Button title={action} onPress={onAction} style={{ marginTop: spacing.sm, alignSelf: 'stretch' }} /> : null}
    </View>
  );
}

/** Error message in the user's language, announced to screen readers as soon as it appears. */
export function ErrorBox({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const { error: describe, t } = useI18n();
  const message = describe(error);
  useEffect(() => { AccessibilityInfo.announceForAccessibility(message); }, [message]);
  return (
    <Card tone="danger" style={{ gap: 10 }}>
      <Row style={{ alignItems: 'flex-start' }}>
        <Icon name="alert-circle" color={useTheme().c.danger} />
        <T tone="danger" weight="medium" style={{ flex: 1 }}>{message}</T>
      </Row>
      {onRetry ? <Button title={t('common.retry')} kind="ghost" icon="refresh" onPress={onRetry} /> : null}
    </Card>
  );
}

const severityTone: Record<string, 'danger' | 'warn' | 'info'> = { allergy: 'danger', contraindicated: 'danger', major: 'danger', moderate: 'warn', minor: 'info' };

/** Clinical alert: severity first (in words, not only colour), then the effect and what to do. */
export function AlertCard({ alert }: { alert: InteractionAlert }) {
  const { c, radius, spacing } = useTheme();
  const { t } = useI18n();
  const tone = severityTone[alert.severity] ?? 'info';
  const border = { danger: c.danger, warn: c.warn, info: c.info }[tone];
  return (
    <View accessible style={{ backgroundColor: c.surface, borderRadius: radius.lg, borderWidth: StyleSheet.hairlineWidth, borderColor: c.border, borderStartWidth: 5, borderStartColor: border, padding: spacing.lg, gap: 6 }}>
      <Row wrap>
        <Badge label={t(`severity.${alert.severity}` as never)} tone={tone} icon={tone === 'danger' ? 'warning' : 'information-circle'} />
        <T v="label">{alert.drugA} + {alert.drugB}</T>
      </Row>
      <T>{alert.effect}</T>
      <T v="small" tone="muted">{alert.management}</T>
    </View>
  );
}

/** Quantity stepper. Screen readers treat it as one adjustable control (swipe up/down to change). */
export function Stepper({ value, min = 1, max, onChange, label }: { value: number; min?: number; max: number; onChange: (n: number) => void; label: string }) {
  const { c, radius } = useTheme();
  const { t, number } = useI18n();
  return (
    <View
      accessible
      accessibilityRole="adjustable"
      accessibilityLabel={label}
      accessibilityValue={{ min, max, now: value, text: number(value) }}
      accessibilityActions={[{ name: 'increment' }, { name: 'decrement' }]}
      onAccessibilityAction={(e) => onChange(e.nativeEvent.actionName === 'increment' ? Math.min(max, value + 1) : Math.max(min, value - 1))}
      style={[styles.stepper, { borderColor: c.border, borderRadius: radius.md, backgroundColor: c.surface }]}>
      <IconButton icon="remove" label={t('product.decrease')} onPress={() => onChange(Math.max(min, value - 1))} color={value <= min ? c.border : c.primary} />
      <T v="h2" style={{ minWidth: 28, textAlign: 'center' }}>{number(value)}</T>
      <IconButton icon="add" label={t('product.increase')} onPress={() => onChange(Math.min(max, value + 1))} color={value >= max ? c.border : c.primary} />
    </View>
  );
}

/** Single-choice group (language, appearance, text size...). Each option is announced as a radio button. */
export function Segmented<V extends string>({ options, value, onChange, label }: { options: { value: V; label: string; icon?: IconName }[]; value: V; onChange: (v: V) => void; label: string }) {
  const { c, radius, type, spacing } = useTheme();
  return (
    <View accessibilityRole="radiogroup" accessibilityLabel={label} style={{ gap: spacing.sm }}>
      {options.map((o) => {
        const selected = o.value === value;
        return (
          <Pressable
            key={o.value}
            accessibilityRole="radio"
            accessibilityState={{ checked: selected }}
            accessibilityLabel={o.label}
            onPress={() => onChange(o.value)}
            style={[styles.option, { borderRadius: radius.md, borderColor: selected ? c.primary : c.border, backgroundColor: selected ? c.primarySoft : c.surface, paddingHorizontal: spacing.lg }]}>
            {o.icon ? <Icon name={o.icon} color={selected ? c.primary : c.textMuted} /> : null}
            <Text maxFontSizeMultiplier={1.8} style={[type('body', selected ? 'semibold' : 'regular'), { flex: 1, color: c.text }]}>{o.label}</Text>
            <Icon name={selected ? 'radio-button-on' : 'radio-button-off'} color={selected ? c.primary : c.textMuted} />
          </Pressable>
        );
      })}
    </View>
  );
}

/** Search input with a clear button. With `onPress` it renders as a tappable entry that opens the search screen. */
export function SearchBar({ value, onChangeText, onSubmit, onPress, placeholder, autoFocus }: {
  value?: string; onChangeText?: (s: string) => void; onSubmit?: () => void; onPress?: () => void; placeholder: string; autoFocus?: boolean;
}) {
  const { c, radius, type, spacing, elevation } = useTheme();
  const { t, isRTL } = useI18n();
  const box: StyleProp<ViewStyle> = [styles.search, { borderRadius: radius.pill, borderColor: c.border, backgroundColor: c.surface, paddingHorizontal: spacing.lg }, elevation];
  if (onPress) {
    return (
      <Pressable accessibilityRole="search" accessibilityLabel={placeholder} onPress={onPress} style={({ pressed }) => [box, pressed && { opacity: 0.85 }]}>
        <Icon name="search" color={c.primary} />
        <Text numberOfLines={1} maxFontSizeMultiplier={1.6} style={[type('body'), { color: c.textMuted, flex: 1 }]}>{placeholder}</Text>
      </Pressable>
    );
  }
  return (
    <View style={box}>
      <Icon name="search" color={c.primary} />
      <TextInput
        accessibilityRole="search"
        accessibilityLabel={placeholder}
        value={value}
        onChangeText={onChangeText}
        onSubmitEditing={onSubmit}
        placeholder={placeholder}
        placeholderTextColor={c.textMuted}
        returnKeyType="search"
        autoFocus={autoFocus}
        autoCorrect={false}
        maxFontSizeMultiplier={1.6}
        style={[type('body'), { flex: 1, minWidth: 0, minHeight: touchTarget, textAlign: isRTL ? 'right' : 'left', writingDirection: isRTL ? 'rtl' : 'ltr' }]}
      />
      {value ? <IconButton icon="close-circle" label={t('search.clearInput')} onPress={() => onChangeText?.('')} color={c.textMuted} /> : null}
    </View>
  );
}

// =====================================================================================================
// Product card
// =====================================================================================================

/** Memoised: list rows only re-render when their own product or heart state changes. */
export const ProductCard = memo(function ProductCard({ product, saved, onPress, onPressIn, onToggleSave }: {
  product: ProductSummary; saved?: boolean; onPress: () => void; onPressIn?: () => void; onToggleSave?: () => void;
}) {
  const { c, radius, spacing, elevation } = useTheme();
  const { t, money, number } = useI18n();
  const [failed, setFailed] = useState(false);
  const price = money(product.priceMinor, product.currency);
  // The card and the heart are SIBLING controls (a button may not contain another button): screen readers
  // then reach two clear targets, "open product" and "save", and the web output stays valid HTML.
  return (
    <View style={[{ flex: 1, margin: spacing.xs + 2, backgroundColor: c.surface, borderRadius: radius.lg, borderWidth: StyleSheet.hairlineWidth, borderColor: c.border }, elevation]}>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={t('product.a11y', { name: product.name, price, rx: product.requiresPrescription ? t('product.a11yRx') : '' })}
        onPress={onPress}
        onPressIn={onPressIn}
        style={({ pressed }) => [{ padding: spacing.sm + 2, borderRadius: radius.lg }, pressed && { opacity: 0.9 }]}>
        <View>
          {failed ? (
            <View style={{ width: '100%', aspectRatio: 1.25, borderRadius: radius.md, backgroundColor: c.surfaceAlt, alignItems: 'center', justifyContent: 'center' }}>
              <Icon name="medkit-outline" size={34} color={c.ornament} />
            </View>
          ) : (
            // expo-image: memory + disk cache, downsampled decode, recycled in virtualised lists.
            <Image source={product.imageUrl} accessible={false} onError={() => setFailed(true)} style={{ width: '100%', aspectRatio: 1.25, borderRadius: radius.md, backgroundColor: c.surfaceAlt }} contentFit="cover" cachePolicy="memory-disk" transition={150} recyclingKey={product.id} />
          )}
          {product.requiresPrescription ? <View style={{ position: 'absolute', top: 8, start: 8 }}><Badge label="Rx" tone="danger" /></View> : null}
        </View>
        <T v="label" numberOfLines={2} style={{ marginTop: spacing.sm }}>{product.name}</T>
        <Row gap={4}>
          <Icon name="star" size={13} color={c.accent} />
          <T v="caption" tone="muted">{number(product.rating)} · {number(product.reviewCount)}</T>
        </Row>
        <T v="h2" tone="accent" style={{ marginTop: 2 }}>{price}</T>
      </Pressable>
      {onToggleSave ? (
        <View style={{ position: 'absolute', top: 6, end: 6 }}>
          <IconButton icon={saved ? 'heart' : 'heart-outline'} label={saved ? t('product.unsave') : t('product.save')} onPress={onToggleSave} color={saved ? c.danger : c.textMuted} filled />
        </View>
      ) : null}
    </View>
  );
});

const styles = StyleSheet.create({
  content: { maxWidth: 760, width: '100%', alignSelf: 'center' },
  button: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 8 },
  iconButton: { width: touchTarget, height: touchTarget, borderRadius: touchTarget / 2, alignItems: 'center', justifyContent: 'center' },
  chip: { flexDirection: 'row', alignItems: 'center', gap: 6, borderWidth: 1, minHeight: 40 },
  badge: { flexDirection: 'row', alignItems: 'center', gap: 4, paddingHorizontal: 9, paddingVertical: 3, alignSelf: 'flex-start' },
  inputWrap: { flexDirection: 'row', alignItems: 'center', borderWidth: 1 },
  input: { flex: 1, minWidth: 0, minHeight: touchTarget, paddingHorizontal: 14, paddingVertical: 10 }, // minWidth 0: web inputs have an intrinsic width
  emptyIcon: { width: 72, height: 72, borderRadius: 36, alignItems: 'center', justifyContent: 'center', borderWidth: 1 },
  stepper: { flexDirection: 'row', alignItems: 'center', borderWidth: 1, alignSelf: 'flex-start' },
  option: { flexDirection: 'row', alignItems: 'center', gap: 12, borderWidth: 1, minHeight: 54 },
  search: { flexDirection: 'row', alignItems: 'center', gap: 10, borderWidth: 1, minHeight: 52 },
});
