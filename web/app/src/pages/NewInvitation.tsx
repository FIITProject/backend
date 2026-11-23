import { useCrumbs } from '../layout/AppLayout';
import { Empty } from '../ui/ui';

/** new-invitation.html — ещё не перенесена. */
export default function NewInvitation() {
  useCrumbs([{ label: 'Новое приглашение' }]);
  return <Empty>Страница «Новое приглашение» в работе</Empty>;
}
