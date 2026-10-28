namespace Auka.Application.Entities;

/// <summary>Set de beneficios pro-retención (Set 1, Set 2...). Cambia cada año.</summary>
public class SetBeneficio
{
    public int Id { get; set; }
    public int ColegioId { get; set; }
    public int Anio { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Articulos { get; set; } = string.Empty; // un artículo por línea
    public int CantidadAEntregar { get; set; }
    public int Entregados { get; set; }
    public int Orden { get; set; }
    public int? ModificadoPorId { get; set; }
    public DateTime? FechaUltimaModificacion { get; set; } // la completa el DbContext
    public bool IsDeleted { get; set; } = false;
}

/// <summary>Prenda o artículo de la bodega del colegio.</summary>
public class ArticuloInventario
{
    public int Id { get; set; }
    public int ColegioId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Tallas { get; set; } = string.Empty;
    public int Stock { get; set; }
    public int Minimo { get; set; } = 3;
    public int? ModificadoPorId { get; set; }
    public DateTime? FechaUltimaModificacion { get; set; }
    public bool IsDeleted { get; set; } = false;
}

/// <summary>Historial de cada ingreso o corrección de stock (quién, cuánto y cuándo).</summary>
public class MovimientoInventario
{
    public int Id { get; set; }
    public int ColegioId { get; set; }
    public int ArticuloInventarioId { get; set; }
    public string Tipo { get; set; } = "Ingreso"; // Ingreso | Ajuste | Entrega
    public int Cantidad { get; set; }             // positivo suma, negativo resta
    public string? Detalle { get; set; }
    public int CreadoPorId { get; set; }
    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;
}
