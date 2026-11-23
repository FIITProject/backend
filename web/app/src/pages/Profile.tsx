import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** profile.html — ещё не перенесена. */
export default function Profile() {
  useCrumbs([{ label: 'Профиль' }]);
  return <Empty>Страница «Профиль» в работе</Empty>;
}
