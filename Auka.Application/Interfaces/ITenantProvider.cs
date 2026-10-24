namespace Auka.Application.Interfaces;

public interface ITenantProvider
{
    /// <summary>Colegio de la sesión actual; 0 si no hay sesión.</summary>
    int TenantId { get; }
}
