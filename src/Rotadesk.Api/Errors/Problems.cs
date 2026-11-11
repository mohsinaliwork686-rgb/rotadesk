using Rotadesk.Domain;

namespace Rotadesk.Api.Errors;

/// <summary>The API's error answers, as ProblemDetails with a <c>code</c> a client can switch on.</summary>
internal static class Problems
{
    public static IResult NotFound(string what) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: $"{what} not found.");

    public static IResult Conflict(string code, string title) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult Forbidden(string title) =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: title);

    /// <summary>A scheduling rule refused the change. Every broken rule is listed, the first one is the title.</summary>
    public static IResult RuleBroken(ScheduleCheck check) =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: check.Violations[0].Message,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = check.Violations[0].Code,
                ["violations"] = check.Violations,
            });
}
