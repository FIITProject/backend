import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** student-applications.html — ещё не перенесена. */
export default function StudentApplications() {
  useCrumbs([{ label: 'Заявки' }]);
  return <Empty>Страница «Заявки» в работе</Empty>;
}
