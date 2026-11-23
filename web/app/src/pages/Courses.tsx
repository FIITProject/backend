import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** home.html — ещё не перенесена. */
export default function Courses() {
  useCrumbs([{ label: 'Курсы' }]);
  return <Empty>Страница «Курсы» в работе</Empty>;
}
