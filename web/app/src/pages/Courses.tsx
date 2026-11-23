import { Link, useSearchParams } from 'react-router';
import { useCrumbs } from '../layout/AppLayout';
import { loadOverview, toReview } from '../lib/courses';
import css from '../styles/pages/courses.css?inline';
import { PageStyle } from '../ui/PageStyle';
import { Async, Empty, useLoad } from '../ui/ui';
import { CourseCard } from './courses/CourseCard';

type Filter = 'all' | 'pending' | 'review';

/** home.html — текущие курсы. ?filter=pending|review (из колокольчика) — только курсы с заявками / с работами на проверке; переключатель показываем только в этом режиме, обычный вид — как в дизайне. */
export default function Courses() {
  useCrumbs([]);
  const [params] = useSearchParams();
  const raw = params.get('filter');
  const filter: Filter = raw === 'pending' || raw === 'review' ? raw : 'all';
  const state = useLoad(loadOverview, []);

  return (
    <>
      <PageStyle css={css} />
      <div className="courses-head">
        <div>
          <h1 className="fs-48">Курсы</h1>
        </div>
        <span className="spacer" />
        <Link className="btn-primary h-58" to="/courses/new">+ Создать курс</Link>
      </div>
      <Async state={state}>
        {o => {
          const review = new Map(o.courses.map(c => [c.key, (o.homeworks.get(c.key) ?? []).reduce((s, h) => s + toReview(h), 0)]));
          const list = o.courses.filter(c => filter === 'all' || (filter === 'pending' ? c.pending > 0 : review.get(c.key)! > 0));
          const counts: Record<Filter, number> = {
            all: o.courses.length,
            pending: o.courses.filter(c => c.pending > 0).length,
            review: o.courses.filter(c => review.get(c.key)! > 0).length,
          };
          const tabs: { key: Filter; label: string; to: string }[] = [
            { key: 'all', label: 'Все', to: '/courses' },
            { key: 'pending', label: 'С заявками', to: '/courses?filter=pending' },
            { key: 'review', label: 'На проверке', to: '/courses?filter=review' },
          ];
          return (
            <>
              {filter !== 'all' && (
                <div>
                  <nav aria-label="Фильтр курсов" className="seg">
                    {tabs.map(t => (
                      <Link aria-current={filter === t.key ? 'page' : undefined} className={`seg__item${filter === t.key ? ' is-active' : ''}`} key={t.key} to={t.to}>
                        {t.label}<span className="seg__count">{counts[t.key]}</span>
                      </Link>
                    ))}
                  </nav>
                </div>
              )}
              {list.length === 0
                ? (
                  <Empty>
                    {o.courses.length === 0
                      ? <>Текущих курсов пока нет. <Link to="/courses/new">Создайте первый курс</Link></>
                      : filter === 'pending' ? 'Новых заявок нет' : 'Работ на проверку нет'}
                  </Empty>
                )
                : (
                  <div className="course-grid">
                    {list.map(c => <CourseCard course={c} index={o.courses.indexOf(c)} key={c.key} toReview={review.get(c.key)!} />)}
                  </div>
                )}
            </>
          );
        }}
      </Async>
    </>
  );
}
