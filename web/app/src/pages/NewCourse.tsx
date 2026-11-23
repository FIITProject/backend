import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** new-course.html — ещё не перенесена. */
export default function NewCourse() {
  useCrumbs([{ label: 'Новый курс' }]);
  return <Empty>Страница «Новый курс» в работе</Empty>;
}
