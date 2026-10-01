namespace Eizo;

internal sealed class ConfigBackupException : Exception
{
    public ConfigBackupException(
        string code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

