import { useState } from 'react';
import { View } from 'react-native';

import { useAssistant } from '@/features/staff';
import { useI18n, type Key } from '@/i18n';
import { useTheme } from '@/theme';
import { Badge, Button, Card, Chip, ErrorBox, Field, Loading, Ornament, Row, Screen, T } from '@/ui/kit';

type Turn = { role: 'user' | 'assistant'; content: string; tools?: string[] };
const SUGGESTIONS: Key[] = ['assistant.s1', 'assistant.s2', 'assistant.s3', 'assistant.s4'];

/** Admin analytics assistant: multi-turn chat; each answer lists the read-only reports it used. */
export default function AssistantScreen() {
  const { t } = useI18n();
  const { c } = useTheme();
  const [turns, setTurns] = useState<Turn[]>([]);
  const [input, setInput] = useState('');
  const assistant = useAssistant();

  const send = (text: string) => {
    if (text.length < 2 || assistant.isPending) return;
    const next: Turn[] = [...turns, { role: 'user', content: text }];
    setTurns(next);
    setInput('');
    // Only the last 10 turns are sent: enough context for follow-ups, bounded tokens per request.
    assistant.mutate(next.slice(-10).map(({ role, content }) => ({ role, content })), {
      onSuccess: (r) => setTurns((all) => [...all, { role: 'assistant', content: r.answer, tools: r.toolCalls.map((x) => x.name) }]),
      onError: () => setTurns((all) => all.slice(0, -1)),
    });
  };

  return (
    <Screen>
      <T tone="muted">{t('assistant.intro')}</T>
      <Ornament />
      {turns.length === 0 ? <Row wrap>{SUGGESTIONS.map((s) => <Chip key={s} label={t(s)} icon="chatbubble-ellipses-outline" onPress={() => send(t(s))} />)}</Row> : null}
      {turns.map((turn, i) => (
        <View key={i} style={{ alignSelf: turn.role === 'user' ? 'flex-end' : 'stretch', maxWidth: turn.role === 'user' ? '88%' : '100%' }}>
          <Card tone={turn.role === 'user' ? 'soft' : 'surface'} label={`${turn.role === 'user' ? t('assistant.you') : t('assistant.bot')}: ${turn.content}`}>
            <T>{turn.content}</T>
            {turn.tools && turn.tools.length > 0 ? (
              <Row wrap><T v="caption" tone="muted">{t('assistant.dataUsed')}:</T>{[...new Set(turn.tools)].map((n) => <Badge key={n} label={n} tone="neutral" />)}</Row>
            ) : null}
          </Card>
        </View>
      ))}
      {assistant.isPending ? <Loading label={t('assistant.working')} /> : null}
      {assistant.isError ? <ErrorBox error={assistant.error} /> : null}
      <View style={{ borderTopWidth: 0.5, borderTopColor: c.border, paddingTop: 12, gap: 10 }}>
        <Field label={t('assistant.question')} value={input} onChangeText={setInput} multiline placeholder={t('assistant.placeholder')} />
        <Button title={t('assistant.send')} icon="send" onPress={() => send(input.trim())} disabled={input.trim().length < 2 || assistant.isPending} />
      </View>
    </Screen>
  );
}
