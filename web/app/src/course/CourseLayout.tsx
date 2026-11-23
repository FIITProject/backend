import { createContext, useContext } from 'react';
import { Link, NavLink, Outlet, useParams } from 'react-router';
import { api } from '../api/api';
import type { CourseDto, SemesterDto } from '../api/types';
import { groupCourses, subjectCoursePath, type SubjectCourse } from '../lib/courses';
import { parseSemesterKey, plural, semesterName } from '../lib/format';
import { Alert, Loading, useLoad } from '../ui/ui';

interface CourseContext {
  /** Предмет в семестре со всеми группами (подгруппами), где у меня есть доступ, — и текущими, и архивными. */
  course: SubjectCourse;
  subjectId: number;
  semester: SemesterDto;
  /** Активные (не архивные) подгруппы — в них можно выдавать ДЗ, приглашать и т. д. */
  activeCourses: CourseDto[];
  /** Курс целиком в архиве — только чтение. */
  readOnly: boolean;
  /** Перечитать курсы (после создания подгруппы, архивации и т. п.). */
  reload: () => void;
  /** Базовый путь курса, например /c/3/2026-Autumn. */
  path: string;
}

const Ctx = createContext<CourseContext>(null!);
/** Данные курса для вкладок внутри CourseLayout. */
export const useCourse = () => useContext(Ctx);

const tabs = [
  { to: '', label: 'Задания', end: true },
  { to: 'summary', label: 'Сводная' },
  { to: 'students', label: 'Студенты' },
  { to: 'invites', label: 'Приглашения' },
  { to: 'teachers', label: 'Преподаватели' },
  { to: 'settings', label: 'Настройки' },
];

/** Шапка курса (hero) с вкладками из дизайна; вкладки — вложенные маршруты. */
export function CourseLayout() {
  const params = useParams();
  const subjectId = Number(params.subjectId);
  const semester = parseSemesterKey(params.semester);
  const state = useLoad(async () => {
    if (!semester) throw new Error('Неверный адрес курса');
    const [active, archived] = await Promise.all([
      api.courses({ subjectId, semester }),
      api.courses({ subjectId, semester, archived: true }),
    ]);
    return groupCourses([...active, ...archived])[0] ?? null;
  }, [params.subjectId, params.semester]);

  if (state.error) return <Alert>{state.error}</Alert>;
  if (state.data === undefined || !semester) return <Loading />;
  if (state.data === null)
    return <Alert>Курс не найден или у вас нет к нему доступа. <Link to="/courses">К списку курсов</Link></Alert>;

  const course = state.data;
  const activeCourses = course.courses.filter(c => !c.isArchived);
  const path = subjectCoursePath(course);
  const groups = new Set(course.courses.map(c => c.groupName)).size;
  return (
    <Ctx.Provider value={{ course, subjectId, semester, activeCourses, readOnly: activeCourses.length === 0, reload: state.reload, path }}>
      <div className="hero p-26-32 min-h-170 d-flex flex-col">
        <div className="row">
          <div>
            <h1 className="text-on-brand fs-30">{course.subjectName}</h1>
            <div className="row gap-8 mt-10">
              <span className="pill">{semesterName(course.semester)}</span>
              <span className="pill">{groups} {plural(groups, 'группа', 'группы', 'групп')}</span>
              <span className="pill">{course.students} {plural(course.students, 'студент', 'студента', 'студентов')}</span>
              {course.isArchived && <span className="pill">в архиве</span>}
            </div>
          </div>
          <span className="spacer" />
          {activeCourses.length > 0 && <Link className="btn-dark" to={`${path}/tasks/new`}>+ Создать задание</Link>}
        </div>
        <nav aria-label="Разделы курса" className="row gap-8 mt-auto course-tabs">
          {tabs.map(t => (
            <NavLink key={t.to} end={t.end} to={t.to ? `${path}/${t.to}` : path}
              className={({ isActive }) => isActive
                ? 'bg-surface text-brand-strong rounded-999 p-9-18 fw-800 course-tab is-active'
                : 'pill p-9-18 fs-14 course-tab'}>
              {t.label}{t.to === 'students' && course.pending > 0 ? ` · ${course.pending}` : ''}
            </NavLink>
          ))}
        </nav>
      </div>
      <Outlet />
    </Ctx.Provider>
  );
}
