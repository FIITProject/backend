import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** invitations.html — ещё не перенесена. */
export default function Invitations() {
  useCrumbs([{ label: 'Приглашения' }]);
  return <Empty>Страница «Приглашения» в работе</Empty>;
}
