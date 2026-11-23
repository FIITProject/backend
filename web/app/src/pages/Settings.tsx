import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** settings.html — ещё не перенесена. */
export default function Settings() {
  useCrumbs([{ label: 'Настройки' }]);
  return <Empty>Страница «Настройки» в работе</Empty>;
}
