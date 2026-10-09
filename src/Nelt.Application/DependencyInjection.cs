using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nelt.Application.Common;
using Nelt.Application.Features.Access;
using Nelt.Application.Features.Assignments;
using Nelt.Application.Features.Attendance;
using Nelt.Application.Features.Catalog;
using Nelt.Application.Features.Certificates;
using Nelt.Application.Features.Courses;
using Nelt.Application.Features.Dashboard;
using Nelt.Application.Features.Enrollments;
using Nelt.Application.Features.Events;
using Nelt.Application.Features.Files;
using Nelt.Application.Features.Learning;
using Nelt.Application.Features.Lessons;
using Nelt.Application.Features.Levels;
using Nelt.Application.Features.Materials;
using Nelt.Application.Features.Progress;
using Nelt.Application.Features.Quizzes;
using Nelt.Application.Features.Settings;
using Nelt.Application.Features.Users;

namespace Nelt.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddSingleton<ContentCache>();
        services.AddSingleton<IPlatformTime, PlatformTime>();

        services.AddScoped<ICourseAccess, CourseAccess>();
        services.AddScoped<PerformanceCalculator>();

        services.AddScoped<IPlatformSettingsService, PlatformSettingsService>();
        services.AddScoped<ILevelService, LevelService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<ICourseAdminService, CourseAdminService>();
        services.AddScoped<ILessonService, LessonService>();
        services.AddScoped<IEnrollmentService, EnrollmentService>();
        services.AddScoped<ILearningService, LearningService>();
        services.AddScoped<IQuizAuthoringService, QuizAuthoringService>();
        services.AddScoped<IQuizTakingService, QuizTakingService>();
        services.AddScoped<IAssignmentService, AssignmentService>();
        services.AddScoped<IStudentAssignmentService, StudentAssignmentService>();
        services.AddScoped<IMaterialService, MaterialService>();
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IClassSessionService, ClassSessionService>();
        services.AddScoped<IBiometricIngestionService, BiometricIngestionService>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<IAttendanceFinalizer, AttendanceFinalizer>();
        services.AddScoped<IProgressService, ProgressService>();
        services.AddScoped<ICertificateService, CertificateService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IFileAccessService, FileAccessService>();
        return services;
    }
}
