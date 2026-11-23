import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** dashboard.html — ещё не перенесена. */
export default function Dashboard() {
  useCrumbs([{ label: 'Обзор' }]);
  return <Empty>Страница «Обзор» в работе</Empty>;
}
