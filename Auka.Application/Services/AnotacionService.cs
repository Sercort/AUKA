using Auka.Application.Entities;
using Auka.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Auka.Application.Services;

public interface IAnotacionService
{
    Task<bool> EliminarAnotacionConAuditoriaAsync(int anotacionId, int usuarioQueEliminaId, string motivoEliminacion);
}

public class AnotacionService : IAnotacionService
{
    private readonly IUnitOfWork _unitOfWork;

    public AnotacionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> EliminarAnotacionConAuditoriaAsync(int anotacionId, int usuarioQueEliminaId, string motivoEliminacion)
    {
        await _unitOfWork.BeginTransactionAsync();
        try
        {
            var anotacionesRepo = _unitOfWork.Repository<Anotacion>();
            var auditoriaRepo = _unitOfWork.Repository<AnotacionEliminada>();

            var anotacion = await anotacionesRepo.Query()
                .FirstOrDefaultAsync(a => a.Id == anotacionId);

            if (anotacion == null) return false;

            var registroAuditoria = new AnotacionEliminada
            {
                AnotacionId = anotacion.Id,
                EstudianteId = anotacion.EstudianteId,
                UsuarioEliminoId = usuarioQueEliminaId,
                MotivoEliminacion = motivoEliminacion,
                FechaEliminacion = DateTime.UtcNow,
                ContenidoOriginal = anotacion.Detalle,
                TipoAnotacion = anotacion.Tipo.ToString() 
            };

            await auditoriaRepo.AddAsync(registroAuditoria);

            anotacionesRepo.Delete(anotacion);

            await _unitOfWork.CompleteAsync();
            await _unitOfWork.CommitTransactionAsync();

            return true;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }
}