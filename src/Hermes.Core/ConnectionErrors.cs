namespace Hermes.Core;

public sealed class HermesException(string message, Exception? inner = null) : IOException(ConnectionErrors.Local(message), inner);
public sealed class ModelServerException(string message) : IOException(message);
public static class ConnectionErrors
{
    public static string Local(string message) => Safety.Redact(message.StartsWith("[HERMES_ERROR]", StringComparison.Ordinal) ? message : "[HERMES_ERROR] " + message);
    public static string Describe(Exception exception, CancellationToken cancellation = default)
    {
        if ((exception is IOException && exception is not HermesException) || exception is HttpRequestException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException)
            return Safety.Redact(exception.Message);
        if (exception is OperationCanceledException && !cancellation.IsCancellationRequested) return Safety.Redact(exception.Message);
        if (exception is OperationCanceledException) return Local("요청을 중단했습니다.");
        return Local(exception.Message);
    }
}
