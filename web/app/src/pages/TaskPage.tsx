import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** task-page.html — ещё не перенесена. */
export default function TaskPage() {
  useCrumbs([{ label: 'Страница задания' }]);
  return <Empty>Страница «Страница задания» в работе</Empty>;
}
