import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** new-task.html — ещё не перенесена. */
export default function TaskEdit() {
  useCrumbs([{ label: 'Новое задание' }]);
  return <Empty>Страница «Новое задание» в работе</Empty>;
}
