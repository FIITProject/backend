import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** teachers.html — ещё не перенесена. */
export default function Teachers() {
  useCrumbs([{ label: 'Преподаватели' }]);
  return <Empty>Страница «Преподаватели» в работе</Empty>;
}
