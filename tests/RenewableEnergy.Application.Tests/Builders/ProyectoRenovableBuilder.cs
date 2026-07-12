using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Enums;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Application.Tests.Builders
{
    /// <summary>
    /// PATRÓN BUILDER para tests: construye proyectos de prueba con valores
    /// por defecto razonables, dejando personalizar SOLO lo relevante para
    /// cada test.
    ///
    /// ¿POR QUÉ UN BUILDER?
    /// Sin él, cada test repetiría las 4+ líneas de Create(...) con datos que
    /// no le importan; y si mañana Create() gana un parámetro, habría que
    /// tocar CIEN tests en lugar de UNA clase. El API fluido, además, hace
    /// legible la intención: `new ProyectoRenovableBuilder().Activado().Build()`.
    /// </summary>
    public sealed class ProyectoRenovableBuilder
    {
        private string _nombre = "Parque Solar Test";
        private TipoEnergia _tipo = TipoEnergia.Solar;
        private decimal _potenciaKW = 500m;
        private double _latitud = 40.4168;
        private double _longitud = -3.7038;
        private bool _activado;

        public ProyectoRenovableBuilder ConNombre(string nombre)
        {
            _nombre = nombre;
            return this;
        }

        public ProyectoRenovableBuilder ConTipo(TipoEnergia tipo)
        {
            _tipo = tipo;
            return this;
        }

        public ProyectoRenovableBuilder ConPotencia(decimal potenciaKW)
        {
            _potenciaKW = potenciaKW;
            return this;
        }

        /// <summary>El proyecto se construye ya activado (admite mediciones).</summary>
        public ProyectoRenovableBuilder Activado()
        {
            _activado = true;
            return this;
        }

        public ProyectoRenovable Build()
        {
            var proyecto = ProyectoRenovable.Create(
                _nombre,
                _tipo,
                PotenciaKW.Create(_potenciaKW),
                Coordenadas.Create(_latitud, _longitud));

            if (_activado)
            {
                proyecto.Activar();
            }

            // Los eventos del arranque no interesan en los tests de handlers.
            proyecto.ClearDomainEvents();

            return proyecto;
        }
    }
}
