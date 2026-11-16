import { FlashList } from '@shopify/flash-list';
import { router, useLocalSearchParams } from 'expo-router';
import { useCallback, useDeferredValue, useMemo, useState } from 'react';
import { Pressable, ScrollView, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import type { ProductSummary } from '@/core/api/schemas';
import { useSession } from '@/core/auth/session';
import { useCart } from '@/features/cart';
import { NEEDS, useCategories, usePrefetchProduct, useProducts, useRecentSearches, useSuggestions, type ProductFilters, type SortOption } from '@/features/catalog';
import { useFavoriteIds, useToggleFavorite } from '@/features/favorites';
import { useI18n, type Key } from '@/i18n';
import { useTheme } from '@/theme';
import { Badge, Button, Card, Chip, Empty, ErrorBox, Icon, IconButton, Loading, ProductCard, Row, SearchBar, SectionHeader, T, type IconName } from '@/ui/kit';

const PRICE_CAPS = [500, 1000, 2000] as const; // cents
const SORTS: SortOption[] = ['relevance', 'price_asc', 'price_desc', 'rating'];

/**
 * Search, designed for everyday users:
 *  - before typing: recent searches and one-tap "common needs" (people search by problem, not molecule);
 *  - while typing: live suggestions (products + matching needs) that tolerate typos and Arabic;
 *  - results: plain-language filters (type, price, in stock nearby, category) and sort, with the
 *    server's understanding shown ("Showing results for ibuprofen", "Products for: Pain & fever").
 */
export default function SearchScreen() {
  const params = useLocalSearchParams<{ q?: string; category?: string; browse?: string }>();
  const { t, category: categoryLabel, money, number } = useI18n();
  const { c, spacing } = useTheme();
  const cart = useCart();
  const recent = useRecentSearches();

  const [text, setText] = useState(params.q ?? '');
  const [submitted, setSubmitted] = useState<string | undefined>(params.q);
  const [filters, setFilters] = useState<Omit<ProductFilters, 'q'>>({ sort: 'relevance', category: params.category });
  const [showFilters, setShowFilters] = useState(false);
  const browsing = submitted !== undefined || !!filters.category || params.browse === '1';

  const typed = useDeferredValue(text);
  const suggestions = useSuggestions(typed !== submitted ? typed : '');
  const query = useMemo<ProductFilters>(() => ({ ...filters, q: submitted || undefined }), [filters, submitted]);
  const results = useProducts(query);
  const items = useMemo(() => results.data?.pages.flatMap((p) => p.items) ?? [], [results.data]);
  const first = results.data?.pages[0];

  const submit = (q: string) => {
    const term = q.trim();
    setText(term);
    setSubmitted(term);
    recent.add(term);
  };
  const activeFilters = [filters.rx !== undefined, !!filters.maxPriceMinor, !!filters.inStockAt, !!filters.category].filter(Boolean).length;

  // ------------------------------------------------------------------ Before / while typing
  const discovery = (
    <ScrollView keyboardShouldPersistTaps="handled" contentContainerStyle={{ padding: spacing.lg, gap: spacing.md }}>
      {text.trim().length >= 2 && text !== submitted ? (
        <>
          <SectionHeader title={t('search.suggestions')} />
          <Card style={{ padding: 0, gap: 0 }}>
            <SuggestionRow icon="search" label={t('search.searchFor', { query: text.trim() })} onPress={() => submit(text)} />
            {(suggestions.data?.categories ?? []).map((id) => (
              <SuggestionRow key={id} icon="grid-outline" label={categoryLabel(id)} onPress={() => { setFilters((f) => ({ ...f, category: id })); setSubmitted(''); }} />
            ))}
            {(suggestions.data?.products ?? []).map((p) => (
              <SuggestionRow key={p.id} icon="medkit-outline" label={p.name} sub={p.requiresPrescription ? t('product.rx') : p.brand} onPress={() => { recent.add(text); router.push(`/product/${p.id}`); }} />
            ))}
          </Card>
          {suggestions.data?.didYouMean ? <T v="small" tone="muted">{t('search.didYouMean', { term: suggestions.data.didYouMean })}</T> : null}
        </>
      ) : (
        <>
          {recent.recent.length > 0 ? (
            <>
              <SectionHeader title={t('search.recent')} action={t('common.clear')} onAction={recent.clear} />
              <Card style={{ padding: 0, gap: 0 }}>
                {recent.recent.map((r) => (
                  <Row key={r} style={{ paddingStart: spacing.lg }}>
                    <Icon name="time-outline" size={20} color={c.textMuted} />
                    <Pressable accessibilityRole="button" accessibilityLabel={r} onPress={() => submit(r)} style={{ flex: 1, minHeight: 48, justifyContent: 'center' }}>
                      <T>{r}</T>
                    </Pressable>
                    <IconButton icon="close" label={`${t('common.remove')} ${r}`} onPress={() => recent.remove(r)} color={c.textMuted} />
                  </Row>
                ))}
              </Card>
            </>
          ) : null}
          <SectionHeader title={t('search.commonNeeds')} />
          <Row wrap>
            {NEEDS.map((n) => <Chip key={n.key} label={t(n.key)} icon={n.icon as IconName} onPress={() => submit(t(n.key))} />)}
          </Row>
        </>
      )}
    </ScrollView>
  );

  // ------------------------------------------------------------------ Results
  const renderItem = useCallback(({ item }: { item: ProductSummary }) => <ResultCard item={item} />, []);
  const resultHeader = (
    <View style={{ paddingHorizontal: spacing.sm, paddingBottom: spacing.sm, gap: spacing.sm }}>
      <Row style={{ justifyContent: 'space-between' }}>
        <View accessibilityLiveRegion="polite" style={{ flexShrink: 1 }}>
          <T v="label">{first ? t('search.results', { count: first.total }) : t('common.loading')}</T>
        </View>
        <Button title={activeFilters ? `${t('search.filters')} · ${number(activeFilters)}` : t('search.filters')} kind={showFilters ? 'secondary' : 'ghost'} icon="options-outline" compact onPress={() => setShowFilters((s) => !s)} />
      </Row>
      {first?.didYouMean ? <Badge tone="info" icon="color-wand-outline" label={t('search.didYouMean', { term: first.didYouMean })} /> : null}
      {first && first.matchedCategories.length > 0 ? <Badge tone="ok" icon="checkmark-circle-outline" label={t('search.matchedNeed', { needs: first.matchedCategories.map(categoryLabel).join('، ') })} /> : null}
      {showFilters ? (
        <Card style={{ gap: spacing.md }}>
          <FilterGroup title={t('search.sort')}>
            {SORTS.map((s) => <Chip key={s} label={t(`search.sort.${s}` as Key)} selected={filters.sort === s} onPress={() => setFilters((f) => ({ ...f, sort: s }))} />)}
          </FilterGroup>
          <FilterGroup title={t('search.type')}>
            <Chip label={t('common.all')} selected={filters.rx === undefined} onPress={() => setFilters((f) => ({ ...f, rx: undefined }))} />
            <Chip label={t('search.type.otc')} selected={filters.rx === false} onPress={() => setFilters((f) => ({ ...f, rx: false }))} />
            <Chip label={t('search.type.rx')} selected={filters.rx === true} onPress={() => setFilters((f) => ({ ...f, rx: true }))} />
          </FilterGroup>
          <FilterGroup title={t('search.price')}>
            <Chip label={t('search.anyPrice')} selected={!filters.maxPriceMinor} onPress={() => setFilters((f) => ({ ...f, maxPriceMinor: undefined }))} />
            {PRICE_CAPS.map((cap) => <Chip key={cap} label={t('search.upTo', { amount: money(cap) })} selected={filters.maxPriceMinor === cap} onPress={() => setFilters((f) => ({ ...f, maxPriceMinor: cap }))} />)}
          </FilterGroup>
          <Chip icon="storefront-outline" label={t('search.inStock')} selected={!!filters.inStockAt} onPress={() => setFilters((f) => ({ ...f, inStockAt: f.inStockAt ? undefined : cart.branchId }))} />
          <CategoryFilter value={filters.category} onChange={(category) => setFilters((f) => ({ ...f, category }))} />
          <Row>
            <Button title={t('search.clearFilters')} kind="ghost" compact onPress={() => setFilters({ sort: 'relevance' })} style={{ flex: 1 }} />
            <Button title={t('search.showResults')} compact onPress={() => setShowFilters(false)} style={{ flex: 1 }} />
          </Row>
        </Card>
      ) : null}
    </View>
  );

  return (
    <SafeAreaView style={{ flex: 1, backgroundColor: c.background }} edges={['left', 'right', 'bottom']}>
      <View style={{ padding: spacing.lg, paddingBottom: spacing.sm }}>
        <SearchBar value={text} onChangeText={(s) => { setText(s); if (!s) setSubmitted(filters.category || params.browse ? '' : undefined); }} onSubmit={() => submit(text)} placeholder={t('shop.searchPlaceholder')} autoFocus={!browsing} />
      </View>
      {!browsing || (text.trim().length >= 2 && text !== submitted) ? discovery : results.isPending ? <Loading /> : results.isError && items.length === 0 ? (
        <View style={{ padding: spacing.lg }}><ErrorBox error={results.error} onRetry={() => results.refetch()} /></View>
      ) : (
        <FlashList
          data={items}
          numColumns={2}
          keyExtractor={(p) => p.id}
          renderItem={renderItem}
          ListHeaderComponent={resultHeader}
          contentContainerStyle={{ paddingHorizontal: spacing.sm, paddingBottom: spacing.xl }}
          onEndReachedThreshold={0.6}
          onEndReached={() => results.hasNextPage && !results.isFetchingNextPage && results.fetchNextPage()}
          ListEmptyComponent={<Empty icon="search-outline" title={t('search.noResults')} body={t('search.noResultsBody')} action={activeFilters ? t('search.clearFilters') : undefined} onAction={() => setFilters({ sort: 'relevance' })} />}
          ListFooterComponent={results.isFetchingNextPage ? <Loading /> : null}
        />
      )}
    </SafeAreaView>
  );
}

function ResultCard({ item }: { item: ProductSummary }) {
  const prefetch = usePrefetchProduct();
  const { user } = useSession();
  const favorites = useFavoriteIds();
  const toggle = useToggleFavorite();
  const saved = favorites.data?.includes(item.id) ?? false;
  return (
    <ProductCard
      product={item}
      saved={saved}
      onPressIn={() => prefetch(item.id)}
      onPress={() => router.push(`/product/${item.id}`)}
      onToggleSave={() => (user ? toggle.mutate({ productId: item.id, saved }) : router.push('/login'))}
    />
  );
}

function SuggestionRow({ icon, label, sub, onPress }: { icon: IconName; label: string; sub?: string; onPress: () => void }) {
  const { c, spacing } = useTheme();
  return (
    <Pressable accessibilityRole="button" accessibilityLabel={sub ? `${label}, ${sub}` : label} onPress={onPress}
      style={({ pressed }) => [{ flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingHorizontal: spacing.lg, minHeight: 52, borderBottomWidth: 0.5, borderBottomColor: c.border }, pressed && { backgroundColor: c.surfaceAlt }]}>
      <Icon name={icon} size={20} color={c.primary} />
      <View style={{ flex: 1, paddingVertical: 8 }}>
        <T numberOfLines={1}>{label}</T>
        {sub ? <T v="caption" tone="muted">{sub}</T> : null}
      </View>
      <Icon name="chevron-forward" size={18} color={c.textMuted} directional />
    </Pressable>
  );
}

function FilterGroup({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <View style={{ gap: 8 }}>
      <T v="label">{title}</T>
      <Row wrap>{children}</Row>
    </View>
  );
}

function CategoryFilter({ value, onChange }: { value?: string; onChange: (id?: string) => void }) {
  const { t, category } = useI18n();
  const categories = useCategories();
  return (
    <FilterGroup title={t('search.category')}>
      <Chip label={t('common.all')} selected={!value} onPress={() => onChange(undefined)} />
      {(categories.data ?? []).map((cat) => <Chip key={cat.id} label={category(cat.id)} selected={value === cat.id} onPress={() => onChange(cat.id)} />)}
    </FilterGroup>
  );
}
