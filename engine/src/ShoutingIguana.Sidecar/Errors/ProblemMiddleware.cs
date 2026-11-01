namespace ShoutingIguana.Sidecar.Errors;

/// <summary>
/// Answers every failed request with <c>application/problem+json</c>. A refusal carries its own problem; anything else
/// is a bug, logged in full and answered with its message only.
/// </summary>
internal sealed class ProblemMiddleware(RequestDelegate next, ILogger<ProblemMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away: there's nobody to answer.
        }
        catch (Exception exception)
        {
            var problem = exception switch
            {
                ProblemException refused => refused.Problem,
                BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => Problems.BodyTooLarge(),
                BadHttpRequestException bad => Problems.BadRequest("The request couldn't be read.", bad.Message).Problem,
                _ => Problems.Internal(exception)
            };

            if (problem.Status >= StatusCodes.Status500InternalServerError)
            {
                logger.LogError(exception, "{Method} {Path} failed", context.Request.Method, context.Request.Path.Value);
            }

            if (context.Response.HasStarted)
            {
                logger.LogWarning("{Method} {Path} failed after its response started: {Problem}", context.Request.Method,
                    context.Request.Path.Value, problem.Title);
                return;
            }

            context.Response.Clear();
            await ProblemWriter.WriteAsync(context, problem).ConfigureAwait(false);
        }
    }
}
