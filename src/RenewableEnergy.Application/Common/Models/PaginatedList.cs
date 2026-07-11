namespace RenewableEnergy.Application.Common.Models
{
    /// <summary>
    /// Página de resultados + metadatos de paginación.
    ///
    /// ¿POR QUÉ EXISTE?
    /// Devolver "toda la tabla" no escala. Toda query de listado de esta
    /// plantilla devuelve una página con la información que el cliente
    /// necesita para pintar la paginación (total, página actual, ¿hay más?).
    /// </summary>
    public sealed class PaginatedList<T>
    {
        public PaginatedList(IReadOnlyList<T> items, int totalCount, int pageNumber, int pageSize)
        {
            Items = items;
            TotalCount = totalCount;
            PageNumber = pageNumber;
            PageSize = pageSize;
            TotalPages = pageSize > 0 ? (int)Math.Ceiling(totalCount / (double)pageSize) : 0;
        }

        /// <summary>Elementos de la página actual.</summary>
        public IReadOnlyList<T> Items { get; }

        /// <summary>Número de página (empezando en 1).</summary>
        public int PageNumber { get; }

        /// <summary>Tamaño de página solicitado.</summary>
        public int PageSize { get; }

        /// <summary>Total de elementos SIN paginar (para calcular páginas).</summary>
        public int TotalCount { get; }

        /// <summary>Número total de páginas.</summary>
        public int TotalPages { get; }

        /// <summary>¿Existe página anterior?</summary>
        public bool HasPreviousPage => PageNumber > 1;

        /// <summary>¿Existe página siguiente?</summary>
        public bool HasNextPage => PageNumber < TotalPages;
    }
}
