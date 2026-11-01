-- Idempotent demo rows for smart_school_school01 only.
-- Re-running this script does not rename accounts and does not duplicate rows.

DO $$
DECLARE
  school uuid := '4326cc92-4bae-45fe-b155-ea7bf6bc27fb';
  branch uuid := '7cbfc359-c903-4fbd-a22f-75c0177e1d66';
  class_id uuid := 'e29e28a9-3f60-4a30-b3d9-edcd73a8dfc5';
  year_id uuid := 'e7931fdd-8f4a-4582-90cd-73de7785588b';
  teacher uuid := '048955e2-0e1c-4ed6-9b40-49a2015eaf09';
  arabic uuid := '2b2d5aa8-3f2b-4afe-958d-6cb19cfb3887';
  math uuid := '54cdae6b-59ff-491e-b057-17b865f9eaba';
  science uuid := '6a1c0e10-1111-4111-8111-111111111101';
  english uuid := '6a1c0e10-1111-4111-8111-111111111102';
  ahmed uuid := '46b456cc-341d-415d-b0b4-7ccfe6530384';
  layan uuid := 'a1000002-0000-4000-8000-000000000002';
  yusuf uuid := 'a1000003-0000-4000-8000-000000000003';
  mariam uuid := 'a1000004-0000-4000-8000-000000000004';
  omar uuid := 'a1000005-0000-4000-8000-000000000005';
  jana uuid := 'a1000006-0000-4000-8000-000000000006';
  kareem uuid := 'a1000007-0000-4000-8000-000000000007';
  nouran uuid := 'a1000008-0000-4000-8000-000000000008';
  hw_add uuid := 'b1000001-0000-4000-8000-000000000001';
  hw_read uuid := 'b1000002-0000-4000-8000-000000000002';
  hw_plant uuid := 'b1000003-0000-4000-8000-000000000003';
  hw_family uuid := 'b1000004-0000-4000-8000-000000000004';
  hw_times uuid := 'b1000005-0000-4000-8000-000000000005';
BEGIN
  IF current_database() <> 'smart_school_school01' THEN
    RAISE EXCEPTION 'demo seed refused for database %', current_database();
  END IF;

  INSERT INTO subjects (id, school_id, name, created_at_utc)
  SELECT science, school, 'علوم', now()
  WHERE NOT EXISTS (SELECT 1 FROM subjects WHERE id = science);

  INSERT INTO subjects (id, school_id, name, created_at_utc)
  SELECT english, school, 'لغة إنجليزية', now()
  WHERE NOT EXISTS (SELECT 1 FROM subjects WHERE id = english);

  INSERT INTO students (id, school_id, branch_id, user_id, student_code, full_name, date_of_birth, created_at_utc)
  SELECT v.id, school, branch, NULL, v.code, v.full_name, v.born, now()
  FROM (VALUES
    (layan, 'S-1002', 'ليان محمود', DATE '2018-03-14'),
    (yusuf, 'S-1003', 'يوسف عادل', DATE '2018-07-02'),
    (mariam, 'S-1004', 'مريم حسن', DATE '2018-11-21'),
    (omar, 'S-1005', 'عمر خالد', DATE '2019-01-09'),
    (jana, 'S-1006', 'جنا عمرو', DATE '2018-05-30'),
    (kareem, 'S-1007', 'كريم سامي', DATE '2018-09-18'),
    (nouran, 'S-1008', 'نورهان طارق', DATE '2019-02-11')
  ) AS v(id, code, full_name, born)
  WHERE NOT EXISTS (SELECT 1 FROM students s WHERE s.id = v.id);

  INSERT INTO enrollments (id, school_id, student_id, class_section_id, academic_year_id, created_at_utc)
  SELECT v.id, school, v.student_id, class_id, year_id, now()
  FROM (VALUES
    ('c1000002-0000-4000-8000-000000000002'::uuid, layan),
    ('c1000003-0000-4000-8000-000000000003'::uuid, yusuf),
    ('c1000004-0000-4000-8000-000000000004'::uuid, mariam),
    ('c1000005-0000-4000-8000-000000000005'::uuid, omar),
    ('c1000006-0000-4000-8000-000000000006'::uuid, jana),
    ('c1000007-0000-4000-8000-000000000007'::uuid, kareem),
    ('c1000008-0000-4000-8000-000000000008'::uuid, nouran)
  ) AS v(id, student_id)
  WHERE NOT EXISTS (SELECT 1 FROM enrollments e WHERE e.id = v.id);

  INSERT INTO teacher_class_assignments (id, school_id, user_id, class_section_id, subject_id, created_at_utc)
  SELECT v.id, school, teacher, class_id, v.subject_id, now()
  FROM (VALUES
    ('d1000001-0000-4000-8000-000000000001'::uuid, science),
    ('d1000002-0000-4000-8000-000000000002'::uuid, english)
  ) AS v(id, subject_id)
  WHERE NOT EXISTS (
    SELECT 1 FROM teacher_class_assignments a
    WHERE a.user_id = teacher AND a.class_section_id = class_id AND a.subject_id = v.subject_id
  );

  INSERT INTO timetable_entries (id, school_id, class_section_id, subject_id, day_of_week, starts_at, ends_at, room, created_at_utc)
  SELECT gen_random_uuid(), school, class_id, v.subject_id, v.day_of_week, v.starts_at, v.ends_at, v.room, now()
  FROM (VALUES
    (0, TIME '10:00', TIME '10:45', science, 'معمل'),
    (0, TIME '11:00', TIME '11:45', english, 'A1'),
    (1, TIME '09:00', TIME '09:45', arabic, 'A1'),
    (1, TIME '10:00', TIME '10:45', science, 'معمل'),
    (2, TIME '08:00', TIME '08:45', english, 'A1'),
    (2, TIME '09:00', TIME '09:45', arabic, 'A1'),
    (2, TIME '10:00', TIME '10:45', math, 'A1'),
    (3, TIME '08:00', TIME '08:45', science, 'معمل'),
    (3, TIME '09:00', TIME '09:45', math, 'A1'),
    (3, TIME '10:00', TIME '10:45', arabic, 'A1'),
    (4, TIME '08:00', TIME '08:45', english, 'A1'),
    (4, TIME '09:00', TIME '09:45', science, 'معمل'),
    (4, TIME '10:00', TIME '10:45', math, 'A1'),
    (6, TIME '08:00', TIME '08:45', arabic, 'A1'),
    (6, TIME '09:00', TIME '09:45', math, 'A1'),
    (6, TIME '10:00', TIME '10:45', science, 'معمل')
  ) AS v(day_of_week, starts_at, ends_at, subject_id, room)
  WHERE NOT EXISTS (
    SELECT 1 FROM timetable_entries t
    WHERE t.class_section_id = class_id
      AND t.day_of_week = v.day_of_week
      AND t.starts_at = v.starts_at
  );

  INSERT INTO attendance_sessions (id, school_id, class_section_id, session_date, created_at_utc)
  SELECT gen_random_uuid(), school, class_id, d::date, now()
  FROM generate_series(DATE '2026-10-04', DATE '2026-10-31', INTERVAL '1 day') AS d
  WHERE (EXTRACT(DOW FROM d) BETWEEN 0 AND 4 OR d::date = DATE '2026-10-31')
    AND NOT EXISTS (
      SELECT 1 FROM attendance_sessions s
      WHERE s.class_section_id = class_id AND s.session_date = d::date
    );

  INSERT INTO attendance_records (id, school_id, session_id, student_id, status, created_at_utc)
  SELECT gen_random_uuid(), school, s.id, st.id,
    CASE
      WHEN st.id = ahmed AND s.session_date = DATE '2026-10-15' THEN 'excused'
      WHEN st.id = ahmed AND s.session_date = DATE '2026-10-20' THEN 'late'
      WHEN st.id = layan AND EXTRACT(DOW FROM s.session_date) = 1 THEN 'late'
      WHEN st.id = yusuf AND s.session_date = DATE '2026-10-22' THEN 'absent'
      WHEN st.id = mariam AND s.session_date = DATE '2026-10-29' THEN 'absent'
      WHEN st.id = omar AND s.session_date = DATE '2026-10-31' THEN 'late'
      WHEN st.id = jana AND s.session_date = DATE '2026-10-08' THEN 'excused'
      WHEN st.id = kareem AND s.session_date = DATE '2026-10-13' THEN 'absent'
      WHEN st.id = nouran AND EXTRACT(DOW FROM s.session_date) = 3 THEN 'late'
      ELSE 'present'
    END,
    now()
  FROM attendance_sessions s
  JOIN enrollments e ON e.class_section_id = s.class_section_id AND e.academic_year_id = year_id
  JOIN students st ON st.id = e.student_id
  WHERE s.school_id = school
    AND s.session_date BETWEEN DATE '2026-10-01' AND DATE '2026-10-31'
    AND NOT EXISTS (
      SELECT 1 FROM attendance_records r
      WHERE r.session_id = s.id AND r.student_id = st.id
    );

  INSERT INTO homework_assignments (
    id, school_id, class_section_id, subject_id, teacher_user_id,
    title, description, instructions, due_on, created_at_utc)
  SELECT v.id, school, class_id, v.subject_id, teacher, v.title, v.description, v.instructions, v.due_on, now()
  FROM (VALUES
    (hw_add, math, 'تمارين الجمع حتى 20',
      'حل التمارين من 1 إلى 8 في كتاب الرياضيات، صفحة 14.',
      'اكتب الناتج بخط واضح، وراجع العد على الأصابع قبل التسليم.',
      DATE '2026-11-05'),
    (hw_read, arabic, 'قراءة درس العائلة',
      'اقرأ فقرة العائلة في كتاب لغتي، صفحة 9.',
      'جهّز جملتين عن أفراد أسرتك للقراءة في الحصة.',
      DATE '2026-10-28'),
    (hw_plant, science, 'تجربة أجزاء النبات',
      'ارسم نبتة وسمّ الجذر والساق والورقة.',
      'يمكن استخدام ورقة بيضاء وقلم رصاص. اكتب اسم كل جزء بجانبه.',
      DATE '2026-11-08'),
    (hw_family, english, 'English paragraph: My Family',
      'Write four sentences about your family.',
      'Use the words mother, father, brother and sister.',
      DATE '2026-11-03'),
    (hw_times, math, 'جدول الضرب 2',
      'احفظ جدول العدد 2 من 1 إلى 10.',
      'اكتب الجدول في الدفتر وراجع العد اثنين اثنين.',
      DATE '2026-11-12')
  ) AS v(id, subject_id, title, description, instructions, due_on)
  WHERE NOT EXISTS (SELECT 1 FROM homework_assignments h WHERE h.id = v.id);

  INSERT INTO homework_submissions (id, school_id, assignment_id, student_id, status, note, submitted_at_utc)
  SELECT v.id, school, v.assignment_id, v.student_id, v.status, v.note, v.submitted_at
  FROM (VALUES
    ('e1000001-0000-4000-8000-000000000001'::uuid, hw_plant, ahmed, 'submitted', NULL, TIMESTAMPTZ '2026-10-30 14:10+00'),
    ('e1000002-0000-4000-8000-000000000002'::uuid, hw_family, ahmed, 'reviewed', 'جمل واضحة. راجع تهجئة brother.', TIMESTAMPTZ '2026-10-29 12:00+00'),
    ('e1000003-0000-4000-8000-000000000003'::uuid, hw_add, layan, 'submitted', NULL, TIMESTAMPTZ '2026-10-31 09:20+00'),
    ('e1000004-0000-4000-8000-000000000004'::uuid, hw_plant, layan, 'submitted', NULL, TIMESTAMPTZ '2026-10-30 15:00+00'),
    ('e1000005-0000-4000-8000-000000000005'::uuid, hw_family, layan, 'reviewing', NULL, TIMESTAMPTZ '2026-10-30 16:40+00'),
    ('e1000006-0000-4000-8000-000000000006'::uuid, hw_plant, yusuf, 'submitted', NULL, TIMESTAMPTZ '2026-10-31 08:05+00'),
    ('e1000007-0000-4000-8000-000000000007'::uuid, hw_family, mariam, 'submitted', NULL, TIMESTAMPTZ '2026-10-29 18:15+00'),
    ('e1000008-0000-4000-8000-000000000008'::uuid, hw_times, omar, 'submitted', NULL, TIMESTAMPTZ '2026-10-31 10:00+00'),
    ('e1000009-0000-4000-8000-000000000009'::uuid, hw_add, nouran, 'reviewing', NULL, TIMESTAMPTZ '2026-10-31 11:25+00'),
    ('e1000010-0000-4000-8000-000000000010'::uuid, hw_read, kareem, 'reviewed', 'القراءة سليمة.', TIMESTAMPTZ '2026-10-27 13:00+00')
  ) AS v(id, assignment_id, student_id, status, note, submitted_at)
  WHERE NOT EXISTS (SELECT 1 FROM homework_submissions s WHERE s.id = v.id);

  IF to_regclass('public.school_trips') IS NOT NULL THEN
    INSERT INTO school_trips (id, school_id, title, subtitle, student_count, supervisor_name, trip_on, status, created_at_utc)
    SELECT v.id, school, v.title, v.subtitle, v.students, 'مشرف الاختبار', v.trip_on, v.status, now()
    FROM (VALUES
      ('f1000001-0000-4000-8000-000000000001'::uuid, 'خط التجمع الأول', 'صباحًا من 07:15 إلى 08:00', 28, DATE '2026-10-31', 'current'),
      ('f1000002-0000-4000-8000-000000000002'::uuid, 'خط مدينة نصر', 'عودة من 13:30 إلى 14:15', 26, DATE '2026-10-30', 'completed'),
      ('f1000003-0000-4000-8000-000000000003'::uuid, 'خط المعادي', 'صباحًا من 07:15 إلى 08:00', 24, DATE '2026-10-28', 'completed')
    ) AS v(id, title, subtitle, students, trip_on, status)
    WHERE NOT EXISTS (SELECT 1 FROM school_trips t WHERE t.id = v.id);
  END IF;
END $$;
