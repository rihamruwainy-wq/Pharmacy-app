import { FlashList } from '@shopify/flash-list';
import { router } from 'expo-router';
import { View } from 'react-native';

import { useSession } from '@/core/auth/session';
import { useFavoriteIds, useFavoriteProducts, useToggleFavorite } from '@/features/favorites';
import { useI18n } from '@/i18n';
import { useTheme } from '@/theme';
import { Empty, ErrorBox, Loading, ProductCard } from '@/ui/kit';

export default function SavedScreen() {
  const { user } = useSession();
  const { t } = useI18n();
  const { c, spacing } = useTheme();
  const ids = useFavoriteIds();
  const products = useFavoriteProducts(ids.data ?? []);
  const toggle = useToggleFavorite();

  if (!user) {
    return <View style={{ flex: 1, backgroundColor: c.background, padding: spacing.lg }}><Empty icon="heart-outline" title={t('saved.loginTitle')} body={t('saved.loginBody')} action={t('common.logIn')} onAction={() => router.push('/login')} /></View>;
  }
  if (ids.isPending || (products.isPending && (ids.data?.length ?? 0) > 0)) return <Loading />;
  if (ids.isError) return <View style={{ padding: spacing.lg }}><ErrorBox error={ids.error} onRetry={() => ids.refetch()} /></View>;
  // Keep saved order and drop items un-hearted optimistically.
  const order = new Map((ids.data ?? []).map((id, i) => [id, i]));
  const items = (products.data ?? []).filter((p) => order.has(p.id)).sort((a, b) => order.get(a.id)! - order.get(b.id)!);

  return (
    <View style={{ flex: 1, backgroundColor: c.background }}>
      <FlashList
        data={items}
        numColumns={2}
        keyExtractor={(p) => p.id}
        contentContainerStyle={{ padding: spacing.sm }}
        renderItem={({ item }) => <ProductCard product={item} saved onPress={() => router.push(`/product/${item.id}`)} onToggleSave={() => toggle.mutate({ productId: item.id, saved: true })} />}
        ListEmptyComponent={<Empty icon="heart-outline" title={t('saved.empty')} body={t('saved.emptyBody')} />}
      />
    </View>
  );
}
