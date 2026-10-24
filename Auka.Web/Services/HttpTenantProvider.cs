using Auka.Application.Interfaces;

namespace Auka.Web.Services;

public class HttpTenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _accessor;

    public HttpTenantProvider(IHttpContextAccessor accessor) => _accessor = accessor;

    public int TenantId =>
        int.TryParse(_accessor.HttpContext?.User.FindFirst("ColegioId")?.Value, out var id) ? id : 0;
}
