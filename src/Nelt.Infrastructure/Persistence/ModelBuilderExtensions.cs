using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nelt.Domain.Common;

namespace Nelt.Infrastructure.Persistence;

internal static class ModelBuilderExtensions
{
    /// <summary>Maps a <see cref="LocalizedText"/> as a complex type: columns {Name}_En, {Name}_Ar, {Name}_De, {Name}_Zh.</summary>
    public static EntityTypeBuilder<T> Localized<T>(this EntityTypeBuilder<T> builder, Expression<Func<T, LocalizedText?>> property, int? maxLength)
        where T : class
    {
        builder.ComplexProperty(property, text =>
        {
            text.IsRequired();
            if (maxLength is { } length)
            {
                text.Property(t => t.En).HasMaxLength(length);
                text.Property(t => t.Ar).HasMaxLength(length);
                text.Property(t => t.De).HasMaxLength(length);
                text.Property(t => t.Zh).HasMaxLength(length);
            }
        });
        return builder;
    }
}
