using ShoutingIguana.Sidecar.Errors.Models;

namespace ShoutingIguana.Sidecar.Errors;

/// <summary>A request the sidecar refuses, with the problem to send back. Thrown by the services, written by the problem middleware.</summary>
internal sealed class ProblemException(ProblemDto problem, Exception? innerException = null) : Exception(problem.Title, innerException)
{
    public ProblemDto Problem { get; } = problem;
}
