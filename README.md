# Nelt — German & Chinese language course platform

ASP.NET Core MVC (.NET 10) platform for selling and running German (CEFR A1–C2) and Chinese (HSK 1–9) courses:
video lessons, level-based materials, quizzes and final exams, assignments, events, fingerprint attendance,
progress tracking, certificates and next-level eligibility. UI in **English, Arabic (RTL), German and Chinese**.

---

## Quick start

**Prerequisites:** .NET 10 SDK and SQL Server (Docker, or any SQL Server instance — set `ConnectionStrings:Default`).

```bash
# Option A — Docker (SQL Server + app)
docker compose up --build                 # http://localhost:8080  (admin@nelt.local / ChangeMe-2026)

# Option B — local (Linux/macOS: start only the database container first)
docker compose up -d db                   # SQL Server on localhost:1433 (used by appsettings.Development.json)
dotnet restore
dotnet ef migrations add InitialCreate -p src/Nelt.Infrastructure -s src/Nelt.Web -o Persistence/Migrations
dotnet run --project src/Nelt.Web         # https://localhost:7180
dotnet test                               # unit + integration tests (needs the db container running)
```

On first start the app creates the schema, the roles, the standard CEFR/HSK levels, starter website copy
in all four languages, and the administrator from `Seed:AdminEmail` / `Seed:AdminPassword`
(development values are in `appsettings.Development.json`; in production set them as environment variables
`Seed__AdminEmail`, `Seed__AdminPassword`, then remove them after the first start).

> **Migrations.** Generate the initial migration once (command above) and commit it. The initializer applies
> pending migrations on start-up; if none exist it falls back to `EnsureCreated` so a fresh clone runs
> immediately — do not rely on that fallback in production.

After signing in as admin: **Platform information** (website texts in 4 languages), **Courses** (create a course,
assign an instructor), **Users** (instructors, students, fingerprint IDs), **Fingerprint devices**.

---

## What is where

| Requirement | Where |
|---|---|
| Landing page with course & platform info, fully admin-managed, 4 languages | `/` · Admin → Platform information, Courses, Levels, Events |
| Animated German element + “Wer zu spät kommt, den bestraft das Leben” | Home: live *Zeitring* (Berlin time on Schwarz/Rot/Gold rings) and the quote band |
| Sell courses for all levels | Course pages → *Reserve my seat* → Admin → Enrollments → *Confirm* (payment at desk / bank transfer); walk-ins via *Enroll a student* |
| Video lessons per course | Teach → Lessons (upload MP4/WebM streamed with range requests, or YouTube/Vimeo); free preview lessons |
| Quizzes for online and in-person students | Teach → Quizzes (audience: all / online / in-person, time limit, attempts, auto-grading, manual score entry for paper quizzes) |
| Assignment submissions | Learn → Assignments (text and/or file) · Teach → Submissions (score + feedback) |
| Books, documents, audio organised per level | Teach → Materials library (tabs per level, grouped by category, ordered, optionally course-specific) |
| Progress per student / lesson reached | Teach → Students & progress (current lesson, completion, quiz/assignment/attendance/final scores, outlook) |
| Events (visits, conversation sessions, delegations, guests, cultural) | Teach → Events · public `/events` · student schedule |
| Attendance + fingerprint device | Teach → Classes & attendance (schedule planner, sheets, matrix) · Admin → Fingerprint devices |
| Final exam, certificate request, next-level eligibility | Quiz kind *Final exam* · Learn → Certificate · Teach → Certificate requests · public `/certificates/verify` |

### Completion & progression rules
Per course (Admin → Course → *Completion & next level*): weights for quizzes / assignments / final exam, passing
score, score for the next level, minimum final-exam score, minimum attendance (in person) and whether online students
must finish every lesson. `Nelt.Domain/Services/CompletionEvaluator.cs` applies them; components a course does not use
are dropped and their weight redistributed; missing results keep the evaluation *pending* rather than failing it.
Approving a certificate re-evaluates the student, snapshots the score and assigns a verifiable serial number.

---

## Survival German (game)

`/survival-german` (linked from the top navigation) is an A1/A2 story game for Arabic-speaking learners:
seven missions from Berlin airport to a new flat in Munich, each 21–25 challenges, with hearts, stars, points and achievements.

- Engine: `src/Nelt.Web/wwwroot/js/survival-german.js` + `css/survival-german.css`. It runs fully in the browser;
  progress is stored in the browser (`localStorage`), so it works signed in or not.
- Content: `src/Nelt.Web/wwwroot/game/missions/m<1-7>.<a1|a2>.json`. Format: `tools/survival-german/SCHEMA.md`.
- After editing content run `python3 tools/survival-german/validate.py`.

## Fingerprint terminals

Attendance is recorded against scheduled class sessions. A check-in inside a session's window (30 min before start
until the end) marks the in-person student **present**, or **late** after the grace period (10 min); both are
configurable under `Attendance` in `appsettings.json`. When a session ends, a background job marks everyone else
**absent**. Instructors can override any status; a late-arriving device upload still upgrades an automatic absence.

Two ways to connect a device:

1. **ZKTeco and compatible terminals (ADMS / “Cloud Server”)** — register the terminal's serial number under
   Admin → Fingerprint devices, then on the terminal set *Comm. → Cloud Server* to this server. Endpoints:
   `GET/POST /iclock/cdata`, `GET /iclock/getrequest`. Many terminals only speak plain HTTP on the LAN: allow it for
   `/iclock` only with `Devices:AllowPlainHttp=true` and keep the device network isolated.
2. **JSON API** for any other device or a bridge service:
   ```http
   POST /api/biometric/punches
   X-Device-Key: nelt_…            (shown once when the device is registered)
   { "punches": [ { "userId": "1024", "time": "2026-10-04T17:58:12" } ] }
   ```

Each student's *Fingerprint ID* (Admin → Users) must equal the user/PIN number enrolled on the terminal.
Uploads are idempotent (unique device/user/time), rate-limited, and every punch is visible in the check-in log.

---

## Architecture

```
src/
  Nelt.Domain           Entities, value objects, pure rules (grading, attendance, completion). No I/O.
  Nelt.Application      Use-case services per feature, authorization (ICourseAccess), DTOs, validation.
  Nelt.Infrastructure   EF Core (SQL Server), Identity, file storage, background worker, seeding.
  Nelt.Web              MVC: public site + areas Learn (students), Teach (instructors), Admin; APIs; UI.
tests/
  Nelt.UnitTests        Domain rules and shared helpers.
  Nelt.IntegrationTests Whole app in memory against a throw-away SQL Server database (every page, every service).
```

* **Data**: EF Core 10 on SQL Server with retrying execution strategy, split queries, UTC everywhere, set-based
  queries for rosters (no N+1), explicit `RESTRICT` foreign keys (no accidental cascades), unique constraints for
  idempotency (enrollments, lesson progress, attendance, punches, submissions).
* **Multilingual content**: admin-managed text is a `LocalizedText` complex type (EN/AR/DE/ZH columns) with English
  fallback; UI strings use `IStringLocalizer` with English keys. Arabic switches the whole layout to RTL.
* **Security**: ASP.NET Core Identity (lockout, security-stamp revalidation, deactivation), role + resource-based
  authorization (instructors only see their courses), antiforgery on every POST, strict CSP without inline scripts,
  rate limiting on sign-in and device endpoints, uploads validated by type/size and stored outside `wwwroot`,
  streamed through authorization checks.
* **Resilience**: global exception handling with localized error pages (ProblemDetails for APIs), start-up retry
  while the database comes up, `/health/live` and `/health/ready`, background job that never crashes the host.
* **Scaling out**: the app is stateless — Data Protection keys are stored in the database so every instance accepts
  the same cookies; point `Storage:RootPath` at shared storage (or implement `IFileStorage` for blob storage);
  the in-memory content cache is short-lived (5 min) and refreshed per instance.
* **Frontend**: hand-written CSS design system and progressive-enhancement JavaScript (no frameworks, works without JS),
  self-hosted OFL fonts (no third-party requests), SVG icon sprite, reduced-motion support.

### Translations
UI strings live in `src/Nelt.Web/Resources/i18n/*.txt` (`English|Deutsch|العربية|中文`).
After editing run `python3 tools/build_resx.py`; `python3 tools/extract_strings.py` lists any string in the code
that has no translation yet (exit code 1 → use it in CI).

---

## Configuration reference

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings:Default` | LocalDB | SQL Server connection |
| `Storage:RootPath` | `App_Data/storage` | Uploaded videos, materials, submissions |
| `Attendance:OpensMinutesBeforeStart` / `LateAfterMinutes` / `FinalizeIntervalMinutes` | 30 / 10 / 5 | Check-in window and absence job |
| `Devices:AllowPlainHttp` | false | Let terminals use HTTP for `/iclock` |
| `Devices:AllowedNetworks` | `[]` (all) | Networks allowed to reach `/iclock`, e.g. `["192.168.1.0/24"]` (terminals identify only by serial number) |
| `Seed:AdminEmail`, `Seed:AdminPassword`, `Seed:AdminName` | — | First administrator |

Time zone and currency are set in Admin → Platform information (default Africa/Tripoli, LYD).
For production put the app behind a TLS-terminating reverse proxy (`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`).

### Before the first production deployment

1. **Generate and commit the initial migration** (the app falls back to `EnsureCreated` without one, and then
   later model changes would never reach the production database):
   ```bash
   dotnet ef migrations add InitialCreate -p src/Nelt.Infrastructure -s src/Nelt.Web -o Persistence/Migrations
   ```
2. Behind nginx / Cloudflare / any proxy set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, otherwise every visitor
   shares the sign-in rate limit and HTTPS redirection can loop.
3. Set `Devices__AllowedNetworks__0=192.168.1.0/24` (your LAN) if fingerprint terminals use `/iclock`.
4. Set `Seed__AdminEmail` / `Seed__AdminPassword` for the first start only, then remove them.
5. Run `dotnet test` with the database container up — the integration tests open every page as every role.
