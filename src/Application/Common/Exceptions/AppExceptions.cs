namespace Application.Common.Exceptions
{
    public sealed class ConflictException(string message) : Exception(message);
    public sealed class AuthenticationFailedException(string message) : Exception(message);
    public sealed class NotFoundException(string message) : Exception(message);
    public sealed class ForbiddenException(string message) : Exception(message);
}