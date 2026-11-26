import { Link } from 'react-router';
import { subjectCoursePath, type SubjectCourse } from '../../lib/courses';
import { plural, termName } from '../../lib/format';

// Общее для home.html, archive.html и dashboard.html: карточка курса и чипы состояния.

const tones = ['lilac', 'sky', 'peach', 'mint'] as const;

/** «Осень 2026» */
export const semesterTag = (c: SubjectCourse) => {
  const t = termName(c.semester.term);
  return `${t[0].toUpperCase()}${t.slice(1)} ${c.semester.year}`;
};

/** «8 групп · 104 студента»; группы — без учёта подгрупп, как в шапке курса. */
export function courseMeta(c: SubjectCourse) {
  const groups = new Set(c.courses.map(x => x.groupName)).size;
  return `${groups} ${plural(groups, 'группа', 'группы', 'групп')} · ${c.students} ${plural(c.students, 'студент', 'студента', 'студентов')}`;
}

/** Чипы «12 на проверке», «3 заявки» или «Всё проверено». */
export function CourseChips({ course, toReview }: { course: SubjectCourse; toReview: number }) {
  return (
    <>
      {toReview > 0 && <span className="cc-chip cc-chip--warn">{toReview} на проверке</span>}
      {course.pending > 0 && <span className="cc-chip">{course.pending} {plural(course.pending, 'заявка', 'заявки', 'заявок')}</span>}
      {toReview === 0 && course.pending === 0 && <span className="cc-chip cc-chip--ok">Всё проверено</span>}
    </>
  );
}

export function CourseCard({ course, index, toReview, archived }: { course: SubjectCourse; index: number; toReview: number; archived?: boolean }) {
  const tone = archived ? 'archived' : tones[index % tones.length];
  return (
    <article className={`course-card course-card--${tone}`}>
      <span aria-hidden="true" className="course-card__orb" />
      <div className="cc-tags"><span className="cc-tag">{semesterTag(course)}</span></div>
      <h2 className="course-card__title">{course.subjectName}</h2>
      <div className="course-card__meta" title={course.courses.map(c => c.subgroupName).join(', ')}>{courseMeta(course)}</div>
      <div className="course-card__foot">
        <CourseChips course={course} toReview={toReview} />
        <Link className="course-card__go" to={subjectCoursePath(course)}>Перейти в курс <i aria-hidden="true">→</i></Link>
      </div>
    </article>
  );
}
