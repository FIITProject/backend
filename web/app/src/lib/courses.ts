import { api } from '../api/api';
import type { CourseDto, HomeworkDto, SemesterDto } from '../api/types';
import { semesterKey } from './format';

/**
 * «Курс» в интерфейсе (как в дизайне) — предмет в семестре со всеми его группами.
 * В API курс — предмет × подгруппа × семестр (CourseDto), поэтому здесь они собираются в один.
 */
export interface SubjectCourse {
  key: string;
  subjectId: number;
  subjectName: string;
  semester: SemesterDto;
  courses: CourseDto[];
  students: number;
  pending: number;
  isArchived: boolean;
}

export const subjectCoursePath = (c: { subjectId: number; semester: SemesterDto }) => `/c/${c.subjectId}/${semesterKey(c.semester)}`;

export function groupCourses(courses: CourseDto[]): SubjectCourse[] {
  const map = new Map<string, SubjectCourse>();
  for (const c of courses) {
    const key = `${c.subjectId}:${semesterKey(c.semester)}`;
    let g = map.get(key);
    if (!g) {
      g = { key, subjectId: c.subjectId, subjectName: c.subjectName, semester: c.semester, courses: [], students: 0, pending: 0, isArchived: true };
      map.set(key, g);
    }
    g.courses.push(c);
    g.students += c.approvedCount;
    g.pending += c.pendingCount;
    g.isArchived &&= c.isArchived;
  }
  return [...map.values()]
    .map(g => ({ ...g, courses: g.courses.sort((a, b) => a.subgroupName.localeCompare(b.subgroupName, 'ru')) }))
    .sort((a, b) => b.semester.year - a.semester.year || (a.semester.term === b.semester.term ? 0 : a.semester.term === 'Autumn' ? -1 : 1)
      || a.subjectName.localeCompare(b.subjectName, 'ru'));
}

/** Работ ждут проверки по ДЗ: сдано, но проверка ещё не отправлена. */
export const toReview = (h: HomeworkDto) => Math.max(0, h.submittedCount - h.reviewedCount);

export interface Overview {
  courses: SubjectCourse[];
  homeworks: Map<string, HomeworkDto[]>;
  pending: number;
  toReview: number;
}

/** Текущие (не архивные) курсы преподавателя и их ДЗ — для обзора, списка курсов и колокольчика. */
export async function loadOverview(): Promise<Overview> {
  const courses = groupCourses(await api.courses());
  const lists = await Promise.all(courses.map(c => api.homeworks(c.subjectId, c.semester)));
  const homeworks = new Map(courses.map((c, i) => [c.key, lists[i]]));
  return {
    courses,
    homeworks,
    pending: courses.reduce((s, c) => s + c.pending, 0),
    toReview: lists.flat().reduce((s, h) => s + toReview(h), 0),
  };
}
