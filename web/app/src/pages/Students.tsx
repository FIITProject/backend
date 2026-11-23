import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** students.html — ещё не перенесена. */
export default function Students() {
  useCrumbs([{ label: 'Студенты' }]);
  return <Empty>Страница «Студенты» в работе</Empty>;
}
