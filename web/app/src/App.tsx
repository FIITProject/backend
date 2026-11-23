import { lazy, Suspense } from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router';
import { RequireAuth, SessionProvider } from './auth/session';
import { CourseLayout } from './course/CourseLayout';
import { AppLayout } from './layout/AppLayout';
import Archive from './pages/Archive';
import ChangePassword from './pages/ChangePassword';
import CourseTasks from './pages/CourseTasks';
import Courses from './pages/Courses';
import Dashboard from './pages/Dashboard';
import Invitations from './pages/Invitations';
import Login from './pages/Login';
import NewCourse from './pages/NewCourse';
import NewInvitation from './pages/NewInvitation';
import Profile from './pages/Profile';
import Settings from './pages/Settings';
import StudentApplications from './pages/StudentApplications';
import Students from './pages/Students';
import Summary from './pages/Summary';
import TaskEdit from './pages/TaskEdit';
import TaskPage from './pages/TaskPage';
import Teachers from './pages/Teachers';
import { Loading, ToastProvider } from './ui/ui';

// Проверка тянет pdf.js — грузим её отдельным чанком, только когда открывают работу
const Review = lazy(() => import('./pages/Review'));

/**
 * Маршруты ↔ страницы дизайна fiit-design:
 *   /login — authorization, / — dashboard, /courses — home, /courses/new — new-course, /archive — archive,
 *   /profile — profile, /profile/password — password-change,
 *   /c/:subjectId/:semester — курс (предмет в семестре, все его группы): course-task, summary, students,
 *     student-applications, invitations, new-invitation, teachers, settings, new-task, task-page,
 *   /review/:homeworkId/:studentId — performance-check / evaluated (отдельная страница без меню).
 */
export default function App() {
  return (
    <ToastProvider>
      <SessionProvider>
        <BrowserRouter>
          <Routes>
            <Route path="/login" element={<Login />} />
            <Route path="/review/:homeworkId/:studentId" element={<RequireAuth><Suspense fallback={<Loading />}><Review /></Suspense></RequireAuth>} />
            <Route element={<RequireAuth><AppLayout /></RequireAuth>}>
              <Route index element={<Dashboard />} />
              <Route path="courses" element={<Courses />} />
              <Route path="courses/new" element={<NewCourse />} />
              <Route path="archive" element={<Archive />} />
              <Route path="profile" element={<Profile />} />
              <Route path="profile/password" element={<ChangePassword />} />
              <Route path="c/:subjectId/:semester" element={<CourseLayout />}>
                <Route index element={<CourseTasks />} />
                <Route path="tasks/new" element={<TaskEdit />} />
                <Route path="tasks/:homeworkId" element={<TaskPage />} />
                <Route path="tasks/:homeworkId/edit" element={<TaskEdit />} />
                <Route path="summary" element={<Summary />} />
                <Route path="students" element={<Students />} />
                <Route path="students/applications" element={<StudentApplications />} />
                <Route path="invites" element={<Invitations />} />
                <Route path="invites/new" element={<NewInvitation />} />
                <Route path="teachers" element={<Teachers />} />
                <Route path="settings" element={<Settings />} />
              </Route>
            </Route>
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </BrowserRouter>
      </SessionProvider>
    </ToastProvider>
  );
}
