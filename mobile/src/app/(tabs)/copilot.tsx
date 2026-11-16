import { useI18n } from '@/i18n';
import { CopilotAsk } from '@/ui/ai';
import { Ornament, Screen, T } from '@/ui/kit';

export default function CopilotScreen() {
  const { t } = useI18n();
  return (
    <Screen>
      <T tone="muted">{t('copilot.intro')}</T>
      <Ornament />
      <CopilotAsk />
    </Screen>
  );
}
