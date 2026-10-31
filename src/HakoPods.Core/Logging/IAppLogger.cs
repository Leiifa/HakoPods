namespace HakoPods.Core.Logging;

/// <summary>Minimal logging seam so Core never depends on a logging framework.</summary>
public interface IAppLogger
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}

/// <summary>Used in tests and in code paths that must not touch disk.</summary>
public sealed class NullAppLogger : IAppLogger
{
    public static readonly NullAppLogger Instance = new();

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
}
