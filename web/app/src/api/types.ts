// TypeScript-зеркало src/Fiit.Contracts (JSON: camelCase, enum-ы строками, даты — ISO-строки в UTC).

export type SemesterTerm = 'Autumn' | 'Spring';
export interface SemesterDto { year: number; term: SemesterTerm }
export interface CourseRef { id: number; name: string }
export interface TagRef { id: number; name: string }

// Auth
export interface LoginResponse { token: string; mustChangePassword: boolean }
export interface MeResponse { id: number; login: string; fullName: string; mustChangePassword: boolean }
export interface TeacherDto {
  id: number; fullName: string; login: string; addedById: number | null; addedByName: string | null;
  createdAt: string; isDisabled: boolean; canDisable: boolean;
}
export interface TempPasswordResponse { teacherId: number; login: string; tempPassword: string }

// Courses
export interface SubjectDto { id: number; name: string }
export interface CourseDto {
  id: number; subjectId: number; subjectName: string; groupName: string; subgroupName: string; semester: SemesterDto;
  adminTeacherId: number; adminTeacherName: string; isAdmin: boolean; isArchived: boolean; approvedCount: number; pendingCount: number;
}
export interface CreateCourseRequest { subjectId: number; group: string; subgroup: number | null; semester: SemesterDto | null }
export interface CourseAccessDto { teacherId: number; fullName: string; login: string }
export type InviteStatus = 'Active' | 'Expired' | 'Closed';
export interface InviteDto {
  id: number; code: string; link: string; subjectId: number; subjectName: string; semester: SemesterDto; courses: CourseRef[];
  expiresAt: string | null; status: InviteStatus; applicationsCount: number; authorName: string; createdAt: string;
}
export type EnrollmentStatus = 'Pending' | 'Approved' | 'Rejected' | 'Removed';
export interface EnrollmentDto {
  id: number; studentId: number; lastName: string; firstName: string; courseId: number; subjectName: string; courseName: string;
  telegramUsername: string | null; status: EnrollmentStatus; createdAt: string; rejectReason: string | null; botBlocked: boolean; tags: TagRef[];
}
export interface TagDto { id: number; name: string; studentsCount: number }

// Homeworks
export type HomeworkTargetKind = 'Student' | 'Tag' | 'Course';
export interface HomeworkTargetDto { kind: HomeworkTargetKind; refId: number }
export interface SubtaskDto { id: number | null; title: string; maxScore: number | null }
export interface HomeworkEditRequest {
  subjectId: number; semester: SemesterDto; title: string; link: string; deadline: string;
  subtasks: SubtaskDto[]; courseIds: number[]; targets: HomeworkTargetDto[]; notifyStudents: boolean;
}
export interface HomeworkDto {
  id: number; subjectId: number; subjectName: string; semester: SemesterDto; title: string; link: string; deadline: string;
  isPublished: boolean; publishedAt: string | null; authorName: string; subtasks: SubtaskDto[]; courses: CourseRef[];
  targets: HomeworkTargetDto[]; recipientsCount: number; submittedCount: number; reviewedCount: number; subtasksLocked: boolean; isReadOnly: boolean;
}
export interface RecipientsPreviewResponse { count: number }
export interface PublishResponse { notified: number }

// Grading
export type CellStatus = 'NotAssigned' | 'NotSubmitted' | 'Overdue' | 'OnReview' | 'Reviewed' | 'ReviewedWithUnsentChanges';
export interface GradebookDto { homeworks: GradebookHomework[]; rows: GradebookRow[] }
export interface GradebookHomework { id: number; title: string; deadline: string; link: string; assignedCount: number; submittedCount: number; reviewedCount: number }
export interface GradebookRow {
  studentId: number; lastName: string; firstName: string; groupName: string; subgroupName: string; courseId: number;
  cells: GradebookCell[]; average: number | null;
}
export interface GradebookCell {
  homeworkId: number; status: CellStatus; score: number | null; isLate: boolean; versions: number; latestSubmissionId: number | null; reviewedBy: string | null;
}
export interface ReviewContextDto {
  homeworkId: number; homeworkTitle: string; homeworkLink: string; deadline: string; subtasks: SubtaskDto[];
  studentId: number; studentName: string; courseName: string; isReadOnly: boolean; submissions: SubmissionDto[];
}
export interface SubmissionDto {
  id: number; version: number; submittedAt: string; isLate: boolean; lateBy: string | null; fileName: string; fileSize: number;
  isLatest: boolean; review: ReviewDto | null;
}
export interface ReviewDto {
  annotations: string; comment: string | null; subtaskScores: SubtaskScoreDto[]; totalOverride: number | null; total: number | null;
  updatedAt: string; sentAt: string | null; hasUnsentChanges: boolean; updatedByName: string; sentByName: string | null;
}
export interface SubtaskScoreDto { subtaskId: number; score: number | null }
export interface SaveReviewRequest {
  annotations: string; comment: string | null; subtaskScores: SubtaskScoreDto[]; totalOverride: number | null; baseUpdatedAt: string | null;
}

/** Коды ошибок из ProblemDetails.code (Fiit.Contracts.ErrorCodes). */
export const ErrorCodes = {
  CourseArchived: 'course_archived',
  PasswordChangeRequired: 'password_change_required',
  ReviewChangedByColleague: 'review_changed',
} as const;
