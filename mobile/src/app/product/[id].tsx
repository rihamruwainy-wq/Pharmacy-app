import { Image } from 'expo-image';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';

import { useSession } from '@/core/auth/session';
import { useCart } from '@/features/cart';
import { useBranches, useProduct, useStock } from '@/features/catalog';
import { useFavoriteIds, useToggleFavorite } from '@/features/favorites';
import { useI18n } from '@/i18n';
import { useTheme } from '@/theme';
import { Badge, Button, Card, ErrorBox, Icon, IconButton, Loading, Ornament, Row, Screen, Stepper, T } from '@/ui/kit';

export default function ProductScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { t, money, number } = useI18n();
  const { c, radius } = useTheme();
  const product = useProduct(id);
  const cart = useCart();
  const branches = useBranches();
  const stock = useStock(cart.branchId, [id]);
  const { user } = useSession();
  const favorites = useFavoriteIds();
  const toggle = useToggleFavorite();
  const [qty, setQty] = useState(1);

  if (product.isPending) return <Loading />;
  if (product.isError) return <Screen><ErrorBox error={product.error} onRetry={() => product.refetch()} /></Screen>;
  const p = product.data;
  const s = stock.data?.[0];
  const branch = branches.data?.find((b) => b.id === cart.branchId);
  const max = Math.max(1, Math.min(p.maxQtyPerOrder, s?.available ?? p.maxQtyPerOrder, 10));
  const saved = favorites.data?.includes(p.id) ?? false;

  return (
    <Screen>
      <Stack.Screen options={{
        title: p.brand,
        headerRight: () => (
          <IconButton icon={saved ? 'heart' : 'heart-outline'} label={saved ? t('product.unsave') : t('product.save')} color={saved ? c.danger : c.primary}
            onPress={() => (user ? toggle.mutate({ productId: p.id, saved }) : router.push('/login'))} />
        ),
      }} />
      <Image source={p.imageUrl} accessible={false} style={{ width: '100%', aspectRatio: 1.6, borderRadius: radius.lg, backgroundColor: c.surfaceAlt }} contentFit="cover" cachePolicy="memory-disk" />
      <T v="title">{p.name}</T>
      <Row wrap>
        {p.requiresPrescription ? <Badge label={t('product.rx')} tone="danger" icon="document-text-outline" /> : <Badge label={t('product.otc')} tone="ok" icon="checkmark-circle-outline" />}
        <Badge label={`${p.form} · ${p.strength}`} tone="neutral" />
      </Row>
      <Row gap={6} >
        <Icon name="star" size={16} color={c.accent} />
        <T v="small" tone="muted">{t('product.rating', { rating: number(p.rating), count: number(p.reviewCount) })}</T>
      </Row>
      <T v="display" tone="accent">{money(p.priceMinor, p.currency)}</T>
      <T>{p.description}</T>
      <Ornament />

      <Card>
        <Row style={{ justifyContent: 'space-between' }}>
          <Row style={{ flexShrink: 1 }}>
            <Icon name="storefront-outline" color={c.primary} />
            <T v="label" style={{ flexShrink: 1 }}>{t('product.availabilityAt', { branch: branch?.name ?? '…' })}</T>
          </Row>
          {stock.isPending ? <T v="small" tone="muted">{t('product.checking')}</T>
            : s?.status === 'out' ? <Badge label={t('product.out')} tone="danger" />
            : s?.status === 'low' ? <Badge label={t('product.onlyLeft', { count: number(s.available) })} tone="warn" />
            : <Badge label={t('product.inStock')} tone="ok" />}
        </Row>
      </Card>

      <Card>
        <T v="label">{t('product.ingredients')}</T>
        {p.activeIngredients.map((i) => <T key={i} tone="muted">• {i}</T>)}
        <T v="small" tone="muted">{t('product.limit', { count: number(p.maxQtyPerOrder) })}</T>
      </Card>

      {p.requiresPrescription ? (
        <Card tone="warn">
          <Row style={{ alignItems: 'flex-start' }}>
            <Icon name="information-circle-outline" color={c.warn} />
            <T style={{ flex: 1 }}>{t('product.rxNote')}</T>
          </Row>
        </Card>
      ) : null}

      <View style={{ gap: 10, marginTop: 4 }}>
        <T v="label">{t('product.quantity')}</T>
        <Row>
          <Stepper value={qty} max={max} onChange={setQty} label={t('product.quantity')} />
          <Button
            title={t('product.add')}
            icon="basket-outline"
            disabled={s?.status === 'out'}
            style={{ flex: 1 }}
            onPress={() => {
              cart.add({ productId: p.id, name: p.name, priceMinor: p.priceMinor, imageUrl: p.imageUrl, requiresPrescription: p.requiresPrescription, maxQty: p.maxQtyPerOrder }, qty);
              router.push('/cart');
            }}
          />
        </Row>
      </View>
    </Screen>
  );
}
