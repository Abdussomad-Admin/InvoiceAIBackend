namespace Application.Common.Interfaces
{
    public interface ICurrentUser
    {
        Guid? UserId { get; }
    }
}