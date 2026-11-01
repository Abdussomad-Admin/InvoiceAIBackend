namespace Application.Common.Interfaces
{
    public interface ITenantWriteBypass
    {
        IDisposable Allow();
    }
}