# Screen traceability

Inspected again on 28 October 2026. Rows below describe the current source. A screen is integrated only when its visible values come from the database through the API and a widget test or live response check. Screens whose domain has no table stay blocked. No sample rows were added.

The running API already exposes:

- `POST /api/v1/auth/login`
- `POST /api/v1/auth/refresh`
- `POST /api/v1/auth/logout`
- `POST /api/v1/auth/change-password`
- `GET /api/v1/me`
- `PUT /api/v1/me/context`
- `GET /api/v1/students/{studentId}/profile`
- `GET /api/v1/students/{studentId}/home`
- `GET /api/v1/students/{studentId}/attendance`
- `GET /api/v1/students/{studentId}/timetable`

Student routes require a student or parent context whose `studentId` matches the route. A school administrator is rejected. `users` and `refresh_tokens` have no row-level security. School-scoped tables do.

## Student and parent

Project: `smart-school-stud`. Routes are in `lib/routes/app_routes.dart`.

| Screen | Route | Fields and actions | Data source | Endpoint | Authorization | Status |
| --- | --- | --- | --- | --- | --- | --- |
| Splash | `/` | Restore session | `SchoolSession.restore` | `POST /api/v1/auth/refresh`, `GET /api/v1/me` | Stored refresh token | Implemented in the client. Needs a linked student or parent. |
| Landing | `/landing` | Navigation only | None | None | Public | No API required |
| Account type | `/accountType` | Choose student or parent | None | None | Public | No API required |
| Student login | `/login` | Username, password, remember me | `SchoolSession.signIn` with role `student` | `POST /api/v1/auth/login`, then `PUT /api/v1/me/context` | Role `student` and a linked student row | Client implemented. Rejects a temporary password and an account with no linked student. |
| Student home | `/home` | Student name and attendance fraction and count from the home response. Deadline cards only when that response includes them. Recent items and weekly tasks have no API, so those sections use the existing empty line. | `GET` home for `SchoolSession.active` | `GET /api/v1/students/{id}/home` | Student context for that id | Integrated. Widget test `test/home_screen_data_test.dart`. |
| Attendance | `/attendance` | Present, absent, late, excused, today status, student name, and the month grid. Year and month filter chrome stays. | `SchoolApi.attendance` | `GET /api/v1/students/{id}/attendance` | Student context | Counts, today status, and grid colors come from the linked student's records. An empty response shows `0` and `لا يوجد تسجيل`. |
| Timetable | `/timetable` | Current lesson and grid subjects from the linked class timetable. The day chips, colors, and time rail stay as the approved frame. | `SchoolApi.timetable` | `GET /api/v1/students/{id}/timetable` | Student or parent context for that student | Empty responses show `—`. Sample subject names are not shown. |
| Parent login | `/parent-login` | Username, password, remember me | `SchoolSession.signIn` with role `parent` | Login and context | Role `parent` and a guardian link | Client implemented. Same password and link rules as the student login. |
| Parent home | `/parent-home` | Linked child name, attendance count, grade, and parent name. Homework, rewards, exams, fees, results, bus, and notification counts have no API, so those values stay unavailable. | One `SchoolApi.home` call. `gradeName` and `viewerName` are on that response. | `GET /api/v1/students/{id}/home` | Parent context whose student id matches the route. Context selection requires a guardian link. | Integrated. The screen no longer calls `me` and `profile`. Widget test `test/parent_home_data_test.dart`. |
| Parent attendance | `/parent-attendance` | Present, absent, late, excused, today status, and the linked child name. Filter chrome stays. | `SchoolApi.attendance` | `GET /api/v1/students/{id}/attendance` | Parent context for the linked child | Counts and today status come from that child. An empty response shows `0` and `لا يوجد تسجيل`. |
| Child switcher | Parent sheet | Child names | `SchoolSession.children` | `PUT /api/v1/me/context` | Parent membership and guardian link | Wired |
| Homework | `/homework`, `/homework-details`, `/homework-expire`, `/parent-homework` | Title, description, instructions, subject, class, grade, teacher, due date, and status. Submit marks the assignment submitted. A passed due date with no submission is closed. A reviewed submission cannot be replaced. | `homework_assignments`, `homework_submissions`, current enrollment | `GET /api/v1/students/{id}/homework` and `POST /api/v1/students/{id}/homework/{assignmentId}/submission` | Student or parent context for that student. The assignment must belong to the student's current class and school. | Integrated for those fields. A signed-in empty list does not keep the sample cards. Widget test `test/homework_data_test.dart`. File upload stays the existing illustration because there is no file store. Draft save stays unavailable because there is no draft column. `/homework-expire` stays the existing closed-state screen. |
| Library | `/library`, `/library-content-search` | Books and subjects | Local widgets | None | None | Missing |
| Exams | `/exams`, `/exam-details`, `/exam-feedback`, `/parent-exams` | Exam list and feedback | Local screens | None | None | Missing |
| Grades | `/grades` | Subject grades | Local screen | None | None | Missing |
| Calendar and weekly plan | `/calendar`, `/weekly-plan`, `/parent-events` | Events and plan rows | Local screens | None | None | Missing |
| Messages | `/messages`, `/parent-messages` | Threads, search, calls | Local screens | None | None | Missing |
| Notifications | `/notifications`, `/parent-notifications` | Notification list | Local screen | None | None | Missing |
| Leaderboard and rewards | `/leaderboard`, `/gift-reward`, `/parent-leaderboard`, `/parent-rewards` | Ranks and gifts | Local screens | None | None | Missing |
| Student profile | `/profile` | Name, grade, class section, and attendance-day count. Homework, rewards, exams, points, and badges have no tables, so those values are `—` or `لا توجد نقاط`. | `students`, `grade_levels`, `class_sections`, `attendance_records` | `GET /api/v1/students/{id}/profile` and `GET /api/v1/students/{id}/home` | Student context for that student | Integrated. Widget test `test/profile_data_test.dart`. |
| Parent profile | `/parent-profile` | Parent name from the home viewer, child name, grade, and attendance count. Email and phone come from `users` through `GET /api/v1/me`. Address, points, homework, exams, and rewards have no tables. | Same student tables plus `users` | `GET /api/v1/students/{id}/home` and `GET /api/v1/me` | Parent context and guardian link | Integrated for those fields. The second fake child is not shown. Widget test in `test/profile_data_test.dart`. |
| Settings, more, legal | `/settings`, `/more`, `/about`, `/privacy`, `/terms`, and the parent copies | Static documents and local switches | None | None | None | No database fields. Not an integration gap. |
| Annual results | `/annual-results`, `/parent-results` | Result rows | Local screen | None | None | Missing |
| Fees and payments | `/parent-fees`, `/parent-payments` | Fee and payment rows | Local screens | None | None | Missing |
| Bus and trips | `/parent-bus`, arrived, emergencies, `/parent-trips`, trip students, driver chat | Map, trip, and chat fields | Local screens | None | None | Missing |
| Asks, leave, visits | `/parent-ask`, `/parent-asks`, `/parent-leave`, `/parent-leaves`, `/parent-visits` | Forms for audience, student, reason, dates | Local screens | None | None | Missing |

## Teacher

Project: `smart-school-teach`. Routes are in `lib/app/router.dart`. No file under `lib` calls HTTP.

| Screen | Route | Fields and actions | Data source | Endpoint | Authorization | Status |
| --- | --- | --- | --- | --- | --- | --- |
| Splash | `/teacher/splash` | Open login | Local navigation | None | None | No API |
| Login | `/teacher/login` | Username, password, remember me | `TeacherSession.signIn` | `POST /api/v1/auth/login`, then `PUT /api/v1/me/context` | Role `teacher` and a membership in the selected school. A student id is rejected. | Client submits credentials. Empty fields stay on the login screen. Supervisor login is unchanged. |
| Home | `/teacher/home` | Teacher name, today's lesson count, and lesson cards for assigned classes. Notification counts have no API. | `GET /api/v1/teacher/home` after a teacher context | `GET /api/v1/teacher/home` | Teacher context. Lessons come from `teacher_class_assignments` joined to that school's timetable. Other schools are excluded. | Rendered home uses the teacher name and assigned lessons. Unassigned timetable rows are not shown. |
| Homework | `/teacher/homework` and subscreens | Create, edit, and delete for an assigned class and subject. The student-solution list and the review save use the submission rows. Score has no column, so a signed-in card shows `—`. | `homework_assignments`, `homework_submissions`, `teacher_class_assignments` | `GET/POST /api/v1/teacher/homework`, `PUT/DELETE /api/v1/teacher/homework/{id}`, `GET /api/v1/teacher/classes`, `GET/PUT .../submissions` | Teacher context. The class and subject must be assigned to that teacher in the selected school. Another class is rejected. A homework with a submission cannot be deleted. | Integrated for those actions. Unsigned screens keep the original sample cards. The correction chat stays local because there is no message table. Attachment bytes are not stored. |
| Exams | `/teacher/exams`, `/teacher/exams/add`, submissions | Exam form and submission list | Local screens | None | None | Missing |
| Grades | `/teacher/grades`, `/teacher/grades/add` | Grade entry | Local screens | None | None | Missing |
| Attendance | `/teacher/attendance` | Present, absent, late, and excused counts, the progress line, and today's student or class line. The month grid, day labels, and year/month filter stay as the approved frame. There is no mark button on this screen. | `TeacherApi.attendance` | `GET /api/v1/teacher/attendance`. Saving a mark is `PUT /api/v1/teacher/classes/{classSectionId}/attendance`. | Teacher context. Classes come from `teacher_class_assignments`. Students must be enrolled in that class and school. | Counts and the today line come from assigned classes. An empty assignment shows `0` and `لا يوجد تسجيل`. Sample student names are not shown. |
| Timeline | `/teacher/timeline` | Current lesson title, time, and class. The grid frame stays. Subject names come from the assigned schedule. | `TeacherApi.schedule` | `GET /api/v1/teacher/schedule` | Teacher context | Empty schedule shows `—`. Sample lessons are not shown. |
| Chat | `/teacher/chat` | Conversations | Local screens | None | None | Missing |
| Rating | `/teacher/rating` | Student rating form | Local screen | None | None | Missing |
| Calendar | `/teacher/calendar` | Calendar | Local screen | None | None | Missing |
| Teacher profile | `/teacher/profile` | Name, email, phone, assigned subjects, class names, and enrolled-student count. Date of birth and home address have no columns, so those values are `—`. | `users`, `timetable_entries` through the teacher schedule, `attendance` roster | `GET /api/v1/me`, `GET /api/v1/teacher/home`, `GET /api/v1/teacher/schedule`, `GET /api/v1/teacher/attendance` | Teacher context. Schedule and roster stay limited to assigned classes. | Integrated for the columns that exist. Widget tests `test/teacher_profile_data_test.dart` and the profile case in `test/role_feature_screens_test.dart`. |

## Supervisor

| Screen | Route | Fields and actions | Data source | Endpoint | Authorization | Status |
| --- | --- | --- | --- | --- | --- | --- |
| Splash | `/supervisor/splash` | Open login | Local navigation | None | None | No API |
| Login | `/supervisor/login` | Username, password, remember me | `SupervisorSession.signIn` | `POST /api/v1/auth/login`, then `PUT /api/v1/me/context` | Role `supervisor` and a membership in the selected school. A student id is rejected. | Client submits credentials. Empty fields stay on the login screen. |
| Home | `/supervisor/home` | Supervisor name, rating, trip totals, and trip rows. Day chips, the trip card shell, shortcut actions, and the tab bar stay as chrome. | `GET /api/v1/supervisor/home` after a supervisor context | `GET /api/v1/supervisor/home` | Supervisor context. Student and teacher contexts are rejected. There is no supervisor assignment or trip table, so `rating` is null and `trips` is empty. | Rendered home uses the supervisor name and zero trip totals. Sample trips, student counts, and the notification count are not shown. |
| Trips | `/supervisor/trips` | Trip cards | Local widgets | None | None | Missing |
| Tracking | `/supervisor/tracking` | Live map | Local map widget | None | None | Missing |
| Students | `/supervisor/students` | Student list | Local screen | None | None | Missing |
| Messages | `/supervisor/messages` | Driver chat | Local screen | None | None | Missing |
| Emergency | `/supervisor/emergency`, ask, result | Emergency form and result | Local screens | None | None | Missing |
| Notifications, settings, account, more, legal | Matching `/supervisor/...` routes | Display and settings | Local screens | None | None | Missing |

## First workflow

The first shared workflow that already has both a screen and an endpoint is student or parent sign-in, context selection, and the home attendance summary. Attendance and timetable rows exist for the linked student. Homework assignments and submissions are stored in `homework_assignments` and `homework_submissions`. Exams, grades, library, messages, fees, bus, trips, notifications, calendar events, leaderboard, and rewards have no tables. Those screens still show their original local content and are blocked until a product model exists. Date of birth exists on `students` and is returned by the profile API. It is not a field on the student profile screen. It does not exist on `users`, so the teacher profile birth date stays `—`. Home address is not stored for any role.

`test/live_student_home_test.dart` signs in through `SchoolSession` and calls the home, attendance, and timetable methods. `test/home_screen_data_test.dart` pumps `HomeScreen` with a scripted home response and checks the rendered name, attendance, empty deadlines, and empty weekly tasks. `test/parent_home_data_test.dart` pumps `ParentHomeScreen`. `test/live_parent_home_test.dart` signs in as the linked parent, renders that home, and checks that another student id is rejected. Teacher sign-in, teacher context selection, and `GET /api/v1/teacher/home` are now part of the teacher home slice. The response contains the signed-in teacher name and an empty lesson list because no teacher class assignment exists. Supervisor sign-in, supervisor context selection, and `GET /api/v1/supervisor/home` return the signed-in supervisor name, a null rating, and an empty trip list because no supervisor assignment or trip model exists.
