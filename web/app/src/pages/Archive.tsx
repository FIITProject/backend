import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** archive.html — ещё не перенесена. */
export default function Archive() {
  useCrumbs([{ label: 'Архив' }]);
  return <Empty>Страница «Архив» в работе</Empty>;
}
