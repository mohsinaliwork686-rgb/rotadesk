namespace Rotadesk.Domain;

/// <summary>A business rule was broken. <see cref="Code"/> is stable, so the API and tests can rely on it.</summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public DomainException()
        : this("domain.error", "A business rule was broken.")
    {
    }

    public DomainException(string message)
        : this("domain.error", message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "domain.error";
    }

    public string Code { get; }
}
