using Nelt.Application.Common;

namespace Nelt.Web.Infrastructure.Mvc;

public static class FormFileExtensions
{
    /// <summary>Adapts an uploaded form file to the framework-neutral application type (null when nothing was chosen).</summary>
    public static FileUpload? ToUpload(this IFormFile? file)
        => file is null || file.Length == 0 ? null : new FileUpload(file.OpenReadStream(), file.FileName, file.Length);
}
