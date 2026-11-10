using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Platform.Media.Settings;

namespace Platform.Media.Controllers;

/// <summary>
/// Raises the limits of one endpoint, the upload, to the configured largest file (<c>Media:MaxFileMegabytes</c>) and a little more for
/// the form around it; every other endpoint keeps the server's small default. It is a filter, not an attribute with a number, because the
/// number is configuration. It runs before the form is read, so a body that is too large is refused while it arrives, not after it
/// has all been received. (ASP.NET's own <c>RequestSizeLimit</c> and <c>RequestFormLimits</c> attributes do these two things with a
/// number written in the code.)
/// </summary>
public sealed class MediaUploadLimitFilter(IOptions<MediaOptions> options, IOptions<FormOptions> formOptions) : IResourceFilter
{
    // The form's own fields and the boundaries between its parts.
    public const long FormOverheadBytes = 256 * 1024;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var limit = options.Value.MaxFileBytes + FormOverheadBytes;

        var body = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (body is { IsReadOnly: false })
        {
            body.MaxRequestBodySize = limit;
        }

        // The size of one part of a multipart body is limited separately. Only if the form has not been read yet.
        var features = context.HttpContext.Features;
        var form = features.Get<IFormFeature>();
        if (form is null || form.Form is null)
        {
            var basis = formOptions.Value;
            features.Set<IFormFeature>(new FormFeature(context.HttpContext.Request, new FormOptions
            {
                BufferBody = basis.BufferBody,
                MemoryBufferThreshold = basis.MemoryBufferThreshold,
                BufferBodyLengthLimit = basis.BufferBodyLengthLimit,
                ValueCountLimit = basis.ValueCountLimit,
                ValueLengthLimit = basis.ValueLengthLimit,
                KeyLengthLimit = basis.KeyLengthLimit,
                MultipartBoundaryLengthLimit = basis.MultipartBoundaryLengthLimit,
                MultipartHeadersCountLimit = basis.MultipartHeadersCountLimit,
                MultipartHeadersLengthLimit = basis.MultipartHeadersLengthLimit,
                MultipartBodyLengthLimit = limit
            }));
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
