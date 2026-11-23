// Все вызовы API сайта преподавателя (docs/architecture.md, раздел «Сайт»).
import { del, download, get, patch, post, put } from './client';
import type {
  CourseAccessDto, CourseDto, CreateCourseRequest, EnrollmentDto, EnrollmentStatus, GradebookDto, HomeworkDto, HomeworkEditRequest,
  HomeworkTargetDto, InviteDto, LoginResponse, MeResponse, PublishResponse, RecipientsPreviewResponse, ReviewContextDto, ReviewDto,
  SaveReviewRequest, SemesterDto, SubjectDto, TagDto, TeacherDto, TempPasswordResponse,
} from './types';

const sem = (s: SemesterDto) => ({ semesterYear: s.year, semesterTerm: s.term });

export const api = {
  // Вход и профиль
  login: (login: string, password: string) => post<LoginResponse>('/auth/login', { login, password }),
  changePassword: (oldPassword: string, newPassword: string) => post<LoginResponse>('/auth/change-password', { oldPassword, newPassword }),
  me: () => get<MeResponse>('/me'),

  // Преподаватели
  teachers: () => get<TeacherDto[]>('/teachers'),
  addTeacher: (fullName: string, login: string) => post<TempPasswordResponse>('/teachers', { fullName, login }),
  resetTeacherPassword: (id: number) => post<TempPasswordResponse>(`/teachers/${id}/reset-password`),
  disableTeacher: (id: number) => post(`/teachers/${id}/disable`),
  enableTeacher: (id: number) => post(`/teachers/${id}/enable`),

  // Предметы, семестры, курсы (курс = предмет × подгруппа × семестр)
  subjects: (q?: string) => get<SubjectDto[]>('/subjects', { q }),
  createSubject: (name: string) => post<SubjectDto>('/subjects', { name }),
  currentSemester: () => get<SemesterDto>('/semesters/current'),
  courses: (filter: { archived?: boolean; subjectId?: number; semester?: SemesterDto } = {}) =>
    get<CourseDto[]>('/courses', { archived: filter.archived, subjectId: filter.subjectId, ...(filter.semester ? sem(filter.semester) : {}) }),
  course: (id: number) => get<CourseDto>(`/courses/${id}`),
  createCourse: (r: CreateCourseRequest) => post<CourseDto>('/courses', r),
  deleteCourse: (id: number) => del(`/courses/${id}`),
  archiveCourses: (courseIds: number[]) => post('/courses/archive', { courseIds }),
  unarchiveCourses: (courseIds: number[]) => post('/courses/unarchive', { courseIds }),
  courseAccess: (id: number) => get<CourseAccessDto[]>(`/courses/${id}/access`),
  grantAccess: (id: number, teacherId: number) => post(`/courses/${id}/access/${teacherId}`),
  revokeAccess: (id: number, teacherId: number) => del(`/courses/${id}/access/${teacherId}`),

  // Приглашения
  invites: () => get<InviteDto[]>('/invites'),
  createInvite: (subjectId: number, courseIds: number[], expiresAt: string | null) => post<InviteDto>('/invites', { subjectId, courseIds, expiresAt }),
  setInviteExpiry: (id: number, expiresAt: string | null) => patch<InviteDto>(`/invites/${id}`, { expiresAt, close: false }),
  closeInvite: (id: number) => patch<InviteDto>(`/invites/${id}`, { expiresAt: null, close: true }),

  // Студенты и заявки
  enrollments: (filter: { courseIds?: number[]; status?: EnrollmentStatus; tagId?: number; q?: string } = {}) =>
    get<EnrollmentDto[]>('/enrollments', filter),
  approve: (ids: number[]) => post('/enrollments/approve', { ids }),
  reject: (ids: number[], reason: string | null) => post('/enrollments/reject', { ids, reason }),
  removeEnrollment: (id: number) => del(`/enrollments/${id}`),

  // Метки (личные у преподавателя)
  tags: () => get<TagDto[]>('/tags'),
  createTag: (name: string) => post<TagDto>('/tags', { name }),
  renameTag: (id: number, name: string) => put<TagDto>(`/tags/${id}`, { name }),
  deleteTag: (id: number) => del(`/tags/${id}`),
  tagStudents: (id: number, studentIds: number[]) => post(`/tags/${id}/students`, { studentIds }),
  untagStudents: (id: number, studentIds: number[]) => post(`/tags/${id}/students/remove`, { studentIds }),

  // ДЗ
  homeworks: (subjectId: number, semester: SemesterDto) => get<HomeworkDto[]>('/homeworks', { subjectId, ...sem(semester) }),
  homework: (id: number) => get<HomeworkDto>(`/homeworks/${id}`),
  createHomework: (r: HomeworkEditRequest) => post<HomeworkDto>('/homeworks', r),
  updateHomework: (id: number, r: HomeworkEditRequest) => put<HomeworkDto>(`/homeworks/${id}`, r),
  deleteHomework: (id: number) => del(`/homeworks/${id}`),
  publishHomework: (id: number) => post<PublishResponse>(`/homeworks/${id}/publish`),
  previewRecipients: (courseIds: number[], targets: HomeworkTargetDto[]) =>
    post<RecipientsPreviewResponse>('/homeworks/preview-recipients', { courseIds, targets }),

  // Сводная таблица
  gradebook: (subjectId: number, semester: SemesterDto, filter: { courseIds?: number[]; group?: string; tagId?: number } = {}) =>
    get<GradebookDto>('/gradebook', { subjectId, ...sem(semester), ...filter }),
  exportGradebook: (subjectId: number, semester: SemesterDto, filter: { courseIds?: number[]; group?: string; tagId?: number } = {}) =>
    download('/gradebook/export.xlsx', { subjectId, ...sem(semester), ...filter }),

  // Проверка
  reviewContext: (homeworkId: number, studentId: number) => get<ReviewContextDto>(`/homeworks/${homeworkId}/students/${studentId}/review`),
  submissionFile: (submissionId: number) => download(`/submissions/${submissionId}/file`),
  reviewedFile: (submissionId: number) => download(`/submissions/${submissionId}/reviewed-file`),
  saveReview: (submissionId: number, r: SaveReviewRequest) => put<ReviewDto>(`/submissions/${submissionId}/review`, r),
  sendReview: (submissionId: number) => post<ReviewDto>(`/submissions/${submissionId}/review/send`),
};
