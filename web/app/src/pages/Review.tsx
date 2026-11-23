import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** performance-check.html, evaluated.html — ещё не перенесена. */
export default function Review() {
  useCrumbs([{ label: 'Проверка работы' }]);
  return <Empty>Страница «Проверка работы» в работе</Empty>;
}
