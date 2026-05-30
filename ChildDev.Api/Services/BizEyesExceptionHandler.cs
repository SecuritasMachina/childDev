using Microsoft.AspNetCore.Diagnostics;

namespace ChildDev.Api.Services;

/// <summary>
/// Forwards unhandled request exceptions to AnalyticsHub (bizeyes), then returns false so the
/// normal exception handling pipeline (ProblemDetails / error page) still runs.
/// </summary>
public class BizEyesExceptionHandler(BizEyesClient bizEyes) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        bizEyes.TrackException(exception, isHandled: false);
        return ValueTask.FromResult(false);
    }
}
