import { Link } from 'react-router';
import type { HomeworkDto } from '../api/types';
import { useMe } from '../auth/session';
import { useCrumbs } from '../layout/AppLayout';
import { loadOverview, subjectCoursePath, toReview, type SubjectCourse } from '../lib/courses';
import { plural, points, shortDateTime } from '../lib/format';
import coursesCss from '../styles/pages/courses.css?inline';
import dashboardCss from '../styles/pages/dashboard.css?inline';
import { PageStyle } from '../ui/PageStyle';
import { Async, useLoad } from '../ui/ui';
import { CourseChips, courseMeta } from './courses/CourseCard';

const DEADLINES = 5;

/** «ДЗ-3» → «ДЗ», «Гробы 2» → «Г»; для кружка в строке списка. */
function badge(title: string) {
  const word = /^[\p{L}]+/u.exec(title.trim())?.[0] ?? '';
  if (!word) return 'ДЗ';
  return word === word.toUpperCase() ? word.slice(0, 3) : word[0].toUpperCase();
}

const maxPoints = (h: HomeworkDto) => h.subtasks.reduce((s, t) => s + (t.maxScore ?? 0), 0);

interface Row { course: SubjectCourse; hw: HomeworkDto }

/** dashboard.html — обзор: приветствие, счётчики и списки по текущим курсам. */
export default function Dashboard() {
  useCrumbs([]);
  const me = useMe();
  const name = me.fullName.split(' ').filter(Boolean)[1] ?? me.fullName;
  const state = useLoad(loadOverview, []);

  return (
    <>
      <PageStyle css={[coursesCss, dashboardCss]} />
      <Async state={state}>
        {o => {
          const rows: Row[] = o.courses.flatMap(course => (o.homeworks.get(course.key) ?? []).map(hw => ({ course, hw })));
          const now = Date.now();
          const deadlines = rows
            .filter(r => r.hw.isPublished && new Date(r.hw.deadline).getTime() >= now)
            .sort((a, b) => a.hw.deadline.localeCompare(b.hw.deadline))
            .slice(0, DEADLINES);
          const waiting = rows.filter(r => toReview(r.hw) > 0).sort((a, b) => toReview(b.hw) - toReview(a.hw));
          const reviewByCourse = new Map(o.courses.map(c => [c.key, (o.homeworks.get(c.key) ?? []).reduce((s, h) => s + toReview(h), 0)]));
          const withPending = o.courses.filter(c => c.pending > 0);
          const pendingLink = withPending.length === 1 ? `${subjectCoursePath(withPending[0])}/students/applications` : '/courses?filter=pending';
          const reviewLink = waiting.length === 1 ? `${subjectCoursePath(waiting[0].course)}/tasks/${waiting[0].hw.id}` : '/courses?filter=review';
          const summary = [
            o.toReview > 0 && `${o.toReview} ${plural(o.toReview, 'работа', 'работы', 'работ')} на проверке`,
            o.pending > 0 && `${o.pending} ${plural(o.pending, 'новая заявка', 'новые заявки', 'новых заявок')}`,
          ].filter(Boolean);
          const taskPath = (r: Row) => `${subjectCoursePath(r.course)}/tasks/${r.hw.id}`;

          return (
            <>
              <div className="row items-end">
                <div>
                  <h1 className="fs-48">Привет, {name}!</h1>
                  <div className="text-muted fs-17 mt-12">
                    {summary.length ? `У тебя ${summary.join(' и ')}` : 'Новых работ на проверку и заявок нет'}
                  </div>
                </div>
                <span className="spacer" />
                <Link className="btn-primary h-58" to="/courses/new">+ Создать курс</Link>
              </div>
              <div className="dash-grid">
                <Link className="stat" to={reviewLink}>
                  <span className="stat__label">Работ на проверке</span><span className="stat__value">{o.toReview}</span><span className="stat__hint">Перейти к проверке →</span>
                </Link>
                <Link className="stat stat--peach" to={pendingLink}>
                  <span className="stat__label">Новых заявок</span><span className="stat__value">{o.pending}</span><span className="stat__hint">Разобрать заявки →</span>
                </Link>
                <Link className="stat stat--sky" to="/courses">
                  <span className="stat__label">Текущих курсов</span><span className="stat__value">{o.courses.length}</span><span className="stat__hint">Все курсы →</span>
                </Link>
              </div>
              <div className="dash-cols">
                <section className="panel">
                  <div className="panel__head">
                    <h2>Мои курсы</h2><Link className="panel__link" to="/courses">Все курсы</Link>
                  </div>
                  {o.courses.length === 0 && <div className="text-muted">Текущих курсов нет. <Link to="/courses/new">Создать курс</Link></div>}
                  {o.courses.map(c => (
                    <Link className="list-row" key={c.key} to={subjectCoursePath(c)}>
                      <span aria-hidden="true" className="list-row__orb" />
                      <div style={{ minWidth: 0 }}>
                        <div className="list-row__title">{c.subjectName}</div>
                        <div className="list-row__sub">{courseMeta(c)}</div>
                      </div>
                      <span className="list-row__chips"><CourseChips course={c} toReview={reviewByCourse.get(c.key) ?? 0} /></span>
                    </Link>
                  ))}
                </section>
                <section className="panel">
                  <div className="panel__head">
                    <h2>Ближайшие сроки</h2>
                  </div>
                  {deadlines.length === 0 && <div className="text-muted">Опубликованных заданий с будущим сроком нет</div>}
                  {deadlines.map(r => (
                    <Link className="list-row" key={r.hw.id} to={taskPath(r)}>
                      <span className="list-row__dot">{badge(r.hw.title)}</span>
                      <div>
                        <div className="list-row__title">{r.hw.title}</div>
                        <div className="list-row__sub">{r.course.subjectName}{maxPoints(r.hw) > 0 && ` · ${points(maxPoints(r.hw))}`}</div>
                      </div>
                      <span className="list-row__when">{shortDateTime(r.hw.deadline)}</span>
                    </Link>
                  ))}
                </section>
              </div>
              <section className="panel">
                <div className="panel__head">
                  <h2>Ждут проверки</h2>
                </div>
                {waiting.length === 0 && <div className="text-muted">Все сданные работы проверены</div>}
                {waiting.map(r => (
                  <Link className="list-row" key={r.hw.id} to={taskPath(r)}>
                    <span className="list-row__dot list-row__dot--warn">{badge(r.hw.title)}</span>
                    <div>
                      <div className="list-row__title">{r.hw.title}</div>
                      <div className="list-row__sub">{r.course.subjectName} · Сдано {r.hw.submittedCount}/{r.hw.recipientsCount} · проверено {r.hw.reviewedCount}</div>
                    </div>
                    <span className="list-row__when">Проверить</span>
                  </Link>
                ))}
              </section>
            </>
          );
        }}
      </Async>
    </>
  );
}
