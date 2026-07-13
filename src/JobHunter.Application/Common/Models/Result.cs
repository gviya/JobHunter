namespace JobHunter.Application.Common.Models
{
    /// <summary>
    /// Clasifica los errores de negocio. El middleware/controller usa este
    /// tipo para elegir el código HTTP SIN que Application sepa nada de HTTP:
    /// Validation → 400, NotFound → 404, Conflict → 409, Forbidden → 403.
    /// </summary>
    public enum ErrorType
    {
        /// <summary>Error genérico no clasificado (HTTP 500).</summary>
        Failure = 0,

        /// <summary>Datos de entrada inválidos (HTTP 400).</summary>
        Validation = 1,

        /// <summary>El recurso no existe (HTTP 404).</summary>
        NotFound = 2,

        /// <summary>Conflicto con el estado actual: nombre duplicado, transición inválida (HTTP 409).</summary>
        Conflict = 3,

        /// <summary>El usuario no tiene permisos (HTTP 403).</summary>
        Forbidden = 4
    }

    /// <summary>
    /// Describe UN error de negocio: código estable (para que los clientes
    /// programen contra él), mensaje humano y tipo (para el código HTTP).
    /// </summary>
    public sealed record Error(string Codigo, string Mensaje, ErrorType Tipo)
    {
        public static Error Failure(string codigo, string mensaje)
        {
            return new Error(codigo, mensaje, ErrorType.Failure);
        }

        public static Error Validation(string codigo, string mensaje)
        {
            return new Error(codigo, mensaje, ErrorType.Validation);
        }

        public static Error NotFound(string codigo, string mensaje)
        {
            return new Error(codigo, mensaje, ErrorType.NotFound);
        }

        public static Error Conflict(string codigo, string mensaje)
        {
            return new Error(codigo, mensaje, ErrorType.Conflict);
        }

        public static Error Forbidden(string codigo, string mensaje)
        {
            return new Error(codigo, mensaje, ErrorType.Forbidden);
        }
    }

    /// <summary>
    /// PATRÓN RESULT: representa el desenlace de una operación SIN lanzar
    /// excepciones para el flujo de negocio.
    ///
    /// ¿POR QUÉ NO USAR EXCEPCIONES PARA TODO?
    /// - Las excepciones son caras (captura de stack trace) y "invisibles"
    ///   en la firma: quien llama no sabe que pueden saltar.
    /// - Un `Result` OBLIGA al consumidor a considerar el caso de fallo:
    ///   el tipo de retorno lo dice explícitamente.
    ///
    /// CONVENCIÓN DE ESTE PROYECTO (ver docs/PATRONES.md):
    /// - Fallos ESPERABLES del negocio (nombre duplicado, recurso no
    ///   encontrado, transición inválida) → Result.Failure.
    /// - Violaciones de invariantes o errores técnicos → excepción, que el
    ///   ExceptionHandlingMiddleware traduce a ProblemDetails.
    /// </summary>
    public class Result
    {
        protected Result(bool isSuccess, Error? error)
        {
            // Invariantes del propio Result: éxito sin error, fallo con error.
            if (isSuccess && error is not null)
            {
                throw new InvalidOperationException("Un resultado de éxito no puede llevar error.");
            }

            if (!isSuccess && error is null)
            {
                throw new InvalidOperationException("Un resultado de fallo debe llevar error.");
            }

            IsSuccess = isSuccess;
            Error = error;
        }

        /// <summary>True si la operación terminó bien.</summary>
        public bool IsSuccess { get; }

        /// <summary>Negación cómoda para guard clauses: if (result.IsFailure) ...</summary>
        public bool IsFailure => !IsSuccess;

        /// <summary>El error, solo presente cuando IsFailure.</summary>
        public Error? Error { get; }

        public static Result Success()
        {
            return new Result(true, null);
        }

        public static Result Failure(Error error)
        {
            return new Result(false, error);
        }

        public static Result<TValue> Success<TValue>(TValue value)
        {
            return new Result<TValue>(value, true, null);
        }

        public static Result<TValue> Failure<TValue>(Error error)
        {
            return new Result<TValue>(default, false, error);
        }
    }

    /// <summary>
    /// Variante genérica del Result que transporta un valor cuando hay éxito.
    /// Acceder a <see cref="Value"/> en un resultado fallido lanza excepción:
    /// es un BUG del consumidor (debió comprobar IsSuccess antes).
    /// </summary>
    public sealed class Result<TValue> : Result
    {
        private readonly TValue? _value;

        internal Result(TValue? value, bool isSuccess, Error? error) : base(isSuccess, error)
        {
            _value = value;
        }

        /// <summary>Valor de la operación. Solo accesible si IsSuccess.</summary>
        public TValue Value => IsSuccess
            ? _value!
            : throw new InvalidOperationException("No se puede acceder al Value de un resultado fallido.");

        // Conversión implícita: permite `return viewModel;` en un handler
        // cuyo tipo de retorno es Result<ViewModel>. Azúcar sintáctico.
        public static implicit operator Result<TValue>(TValue value)
        {
            return Success(value);
        }
    }
}
