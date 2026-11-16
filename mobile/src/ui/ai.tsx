import { useState } from 'react';
import { View } from 'react-native';

import type { CopilotResponse } from '@/core/api/schemas';
import { useCopilot } from '@/features/staff';
import { useI18n } from '@/i18n';
import { useTheme } from '@/theme';
import { AlertCard, Badge, Button, Card, ErrorBox, Field, Loading, Row, T } from './kit';

/** A copilot answer with its sources, so every clinical statement can be traced to the passage it relies on. */
export function CopilotAnswer({ data, showAlerts = true }: { data: CopilotResponse; showAlerts?: boolean }) {
  const { t } = useI18n();
  const { c } = useTheme();
  const [open, setOpen] = useState(false);
  return (
    <View style={{ gap: 8 }}>
      {showAlerts && data.alerts.length > 0 ? (
        <>
          <T v="label">{t('copilot.systemAlerts')}</T>
          {data.alerts.map((a) => <AlertCard key={`${a.drugA}-${a.drugB}`} alert={a} />)}
        </>
      ) : null}
      <Card style={{ borderColor: c.primary, borderWidth: 1 }}>
        <Row><Badge label={data.aiAvailable ? t('copilot.grounded') : t('copilot.unavailable')} tone={data.aiAvailable ? 'ok' : 'warn'} icon="sparkles-outline" /></Row>
        <T>{data.answer}</T>
        {data.citations.length > 0 ? (
          <>
            <Button title={open ? t('copilot.hideSources') : t('copilot.showSources', { count: data.citations.length })} kind="ghost" compact icon="library-outline" onPress={() => setOpen((o) => !o)} />
            {open ? data.citations.map((cit, i) => (
              <View key={`${cit.sourceId}-${i}`} style={{ borderStartWidth: 3, borderStartColor: c.ornament, paddingStart: 10, gap: 2 }}>
                <T v="label">[{i + 1}] {cit.title}</T>
                <T tone="muted">“{cit.quote}”</T>
                <T v="caption" tone="muted">{cit.provenance}</T>
              </View>
            )) : null}
          </>
        ) : data.sources.length > 0 ? (
          <View style={{ gap: 2 }}>
            <T v="label">{t('copilot.relevant')}</T>
            {data.sources.map((s) => <T key={s.id} v="small" tone="muted">• {s.title}</T>)}
          </View>
        ) : null}
        <T v="caption" tone="muted">{t('copilot.disclaimer')}</T>
      </Card>
    </View>
  );
}

export function CopilotAsk({ orderId }: { orderId?: string }) {
  const { t } = useI18n();
  const [question, setQuestion] = useState('');
  const ask = useCopilot();
  return (
    <View style={{ gap: 10 }}>
      <Field label={t('copilot.ask')} value={question} onChangeText={setQuestion} placeholder={orderId ? t('copilot.placeholderOrder') : t('copilot.placeholder')} multiline />
      <Button title={t('copilot.askButton')} icon="sparkles-outline" kind="secondary" loading={ask.isPending} disabled={question.trim().length < 3} onPress={() => ask.mutate({ question: question.trim(), orderId })} />
      {ask.isPending ? <Loading label={t('copilot.thinking')} /> : null}
      {ask.isError ? <ErrorBox error={ask.error} /> : null}
      {ask.data ? <CopilotAnswer data={ask.data} showAlerts={!orderId} /> : null}
    </View>
  );
}
