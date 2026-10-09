namespace Nelt.Domain.Enums;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Instructor = "Instructor";
    public const string Student = "Student";

    /// <summary>Comma list usable in [Authorize(Roles = ...)].</summary>
    public const string Staff = Admin + "," + Instructor;

    public static readonly IReadOnlyList<string> All = [Admin, Instructor, Student];
}
