import { api } from '../api/api';
import { useCrumbs } from '../layout/AppLayout';
import { groupCourses, toReview } from '../lib/courses';
import css from '../styles/pages/courses.css?inline';
import { PageStyle } from '../ui/PageStyle';
import { Async, Empty, useLoad } from '../ui/ui';
import { CourseCard } from './courses/CourseCard';

/** archive.html — архивные курсы. Вернуть из архива можно в настройках курса (там же, где архивируют). */
export default function Archive() {
  useCrumbs([]);
  const state = useLoad(async () => {
    const courses = groupCourses(await api.courses({ archived: true }));
    // Счётчик непроверенных работ — для чипа «Всё проверено»; если ДЗ не загрузились, чип строим без них
    const review = await Promise.all(courses.map(c =>
      api.homeworks(c.subjectId, c.semester).then(hs => hs.reduce((s, h) => s + toReview(h), 0)).catch(() => 0)));
    return courses.map((course, i) => ({ course, toReview: review[i] }));
  }, []);

  return (
    <>
      <PageStyle css={css} />
      <div className="courses-head">
        <div>
          <h1 className="fs-48">Архив</h1>
        </div>
        <span className="spacer" />
      </div>
      <Async state={state}>
        {list => list.length === 0
          ? <Empty>В архиве пока нет курсов</Empty>
          : (
            <div className="course-grid">
              {list.map(({ course, toReview }, i) => <CourseCard archived course={course} index={i} key={course.key} toReview={toReview} />)}
            </div>
          )}
      </Async>
    </>
  );
}
