# Nelt integration tests

These tests start the real application in memory (real MVC pipeline, real Razor views, real EF Core queries)
against a **throw-away SQL Server database** and exercise it the way users do.

They catch what the compiler cannot: LINQ queries EF Core cannot translate to SQL, views that throw,
broken authorization rules, and business rules that do not hold.

## Run

```bash
# 1. Start SQL Server (the same container the app uses in development)
docker compose up -d db

# 2. Run everything (unit + integration tests)
dotnet test

# Only the integration tests, with each test's name in the output
dotnet test tests/Nelt.IntegrationTests --logger "console;verbosity=normal"
```

A database named `NeltTests_<random>` is created for the run and dropped at the end. Your development
database (`Nelt`) is never touched.

To use another SQL Server, set the server part of the connection string (without `Database=`):

```bash
export NELT_TEST_SQL="Server=myhost,1433;User Id=sa;Password=...;TrustServerCertificate=True;MultipleActiveResultSets=true"
```

## What is covered

| File | What it checks |
|---|---|
| `PageSmokeTests` | Opens every page as a visitor, admin, instructor, in-person student, online student and a student with no courses (also in Arabic, German and Chinese). Missing items must give 404, never 500. |
| `SecurityTests` | Login redirects, role separation, instructors limited to their own courses, pending/unenrolled students blocked, file downloads, CSRF tokens, open redirects, security headers. |
| `FormAndApiTests` | Real form posts with antiforgery tokens (create course — the original bug, register, reserve a seat, change language) and the fingerprint endpoints (`/api/biometric/punches`, `/iclock`). |
| `CourseServiceTests` | Course create/update/delete (including the instructor regression), slugs, covers, lessons. |
| `LearningServiceTests` | Student dashboard, schedule (the second regression), course home, lesson progress, previews, catalogue, protected files. |
| `AssessmentServiceTests` | Quiz authoring, taking, grading, attempts, locking, audiences, final-exam rules, paper scores, assignments and grading. |
| `AttendanceServiceTests` | Sessions and weekly series, attendance sheets and matrix, the absence job, fingerprint ingestion, devices. |
| `EnrollmentAndCertificateTests` | Reservations, payment, cancellation, capacity, walk-in enrollment, the full certificate flow and verification. |
| `AdministrationTests` | Levels, users, settings, events, the materials library, dashboards and progress reports. |

When a page fails, the test output shows the URL and the exception text from the error page.
