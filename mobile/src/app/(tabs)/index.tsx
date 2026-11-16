import { FlashList } from '@shopify/flash-list';
import { router } from 'expo-router';
import { useCallback, useMemo } from 'react';
import { Pressable, RefreshControl, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import type { ProductSummary } from '@/core/api/schemas';
import { useSession } from '@/core/auth/session';
import { useCart } from '@/features/cart';
import { CATEGORY_ICONS, useCategories, usePrefetchProduct, useProducts } from '@/features/catalog';
import { useFavoriteIds, useToggleFavorite } from '@/features/favorites';
import { useI18n } from '@/i18n';
import { useTheme } from '@/theme';
import { Button, ErrorBox, Icon, Loading, Ornament, ProductCard, SearchBar, SectionHeader, T, type IconName } from '@/ui/kit';

/** Shop home: one big search entry, "shop by need" tiles, then popular products (virtualised grid). */
export default function ShopScreen() {
  const { t } = useI18n();
  const { c, spacing } = useTheme();
  const categories = useCategories();
  const products = useProducts({ sort: 'relevance' });
  const prefetch = usePrefetchProduct();
  const { user } = useSession();
  const favorites = useFavoriteIds();
  const toggle = useToggleFavorite();
  const { count } = useCart();
  const saved = useMemo(() => new Set(favorites.data ?? []), [favorites.data]);
  const items = useMemo(() => products.data?.pages.flatMap((p) => p.items) ?? [], [products.data]);

  const renderItem = useCallback(({ item }: { item: ProductSummary }) => (
    <ProductCard
      product={item}
      saved={saved.has(item.id)}
      onPressIn={() => prefetch(item.id)}
      onPress={() => router.push(`/product/${item.id}`)}
      onToggleSave={() => (user ? toggle.mutate({ productId: item.id, saved: saved.has(item.id) }) : router.push('/login'))}
    />
  ), [saved, prefetch, user, toggle]);

  const header = (
    <View style={{ padding: spacing.lg, paddingBottom: spacing.sm, gap: spacing.md }}>
      <T v="title">{t('shop.greeting')}</T>
      <SearchBar placeholder={t('shop.searchPlaceholder')} onPress={() => router.push('/search')} />
      {count > 0 ? <Button title={t('shop.basket', { count })} icon="basket-outline" kind="secondary" onPress={() => router.push('/cart')} /> : null}
      <SectionHeader title={t('shop.byNeed')} />
      <View style={styles.grid}>
        {(categories.data ?? []).map((cat) => (
          <CategoryTile key={cat.id} id={cat.id} />
        ))}
      </View>
      <Ornament style={{ marginTop: spacing.sm }} />
      <SectionHeader title={t('shop.popular')} action={t('common.seeAll')} onAction={() => router.push('/search?browse=1')} />
    </View>
  );

  return (
    <SafeAreaView style={{ flex: 1, backgroundColor: c.background }} edges={['left', 'right']}>
      {products.isPending ? <Loading /> : products.isError && items.length === 0 ? (
        <View style={{ padding: spacing.lg }}><ErrorBox error={products.error} onRetry={() => products.refetch()} /></View>
      ) : (
        <FlashList
          data={items}
          numColumns={2}
          keyExtractor={(p) => p.id}
          renderItem={renderItem}
          ListHeaderComponent={header}
          contentContainerStyle={{ paddingHorizontal: spacing.sm, paddingBottom: spacing.xl }}
          onEndReachedThreshold={0.6}
          onEndReached={() => products.hasNextPage && !products.isFetchingNextPage && products.fetchNextPage()}
          refreshControl={<RefreshControl tintColor={c.primary} refreshing={products.isRefetching} onRefresh={() => { void products.refetch(); }} />}
          ListFooterComponent={products.isFetchingNextPage ? <Loading /> : null}
        />
      )}
    </SafeAreaView>
  );
}

function CategoryTile({ id }: { id: string }) {
  const { c, radius, spacing } = useTheme();
  const { category } = useI18n();
  const label = category(id);
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      onPress={() => router.push(`/search?category=${id}`)}
      style={({ pressed }) => [styles.tile, { backgroundColor: c.surface, borderColor: c.border, borderRadius: radius.lg, padding: spacing.sm }, pressed && { opacity: 0.85 }]}>
      <View style={[styles.tileIcon, { backgroundColor: c.primarySoft, borderColor: c.ornament }]}>
        <Icon name={(CATEGORY_ICONS[id] ?? 'medical-outline') as IconName} size={22} color={c.primary} />
      </View>
      <T v="caption" weight="semibold" center numberOfLines={3}>{label}</T>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: 10 },
  tile: { width: '31%', flexGrow: 1, minHeight: 96, alignItems: 'center', justifyContent: 'center', gap: 6, borderWidth: StyleSheet.hairlineWidth },
  tileIcon: { width: 44, height: 44, borderRadius: 22, alignItems: 'center', justifyContent: 'center', borderWidth: StyleSheet.hairlineWidth },
});
