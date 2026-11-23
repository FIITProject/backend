import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** summary.html — ещё не перенесена. */
export default function Summary() {
  useCrumbs([{ label: 'Сводная таблица' }]);
  return <Empty>Страница «Сводная таблица» в работе</Empty>;
}
