using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Nelt.Infrastructure.Persistence;

/// <summary>SQL Server datetime2 has no kind; values are written as UTC and re-tagged as UTC when read.</summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
