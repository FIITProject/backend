import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** course-task.html — ещё не перенесена. */
export default function CourseTasks() {
  useCrumbs([{ label: 'Задания' }]);
  return <Empty>Страница «Задания» в работе</Empty>;
}
