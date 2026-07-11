using AutoMapper;
using RenewableEnergy.Application.Features.Mediciones.Commands.RegistrarMedicion;
using RenewableEnergy.Application.Features.Mediciones.Queries.ObtenerMediciones;
using RenewableEnergy.Application.Features.Proyectos.Commands.ActivarProyecto;
using RenewableEnergy.Application.Features.Proyectos.Commands.ActualizarProyecto;
using RenewableEnergy.Application.Features.Proyectos.Commands.CrearProyecto;
using RenewableEnergy.Application.Features.Proyectos.Queries.ObtenerProyectoPorId;
using RenewableEnergy.Application.Features.Proyectos.Queries.ObtenerProyectos;
using RenewableEnergy.Domain.Entities;

namespace RenewableEnergy.Application.Mappings
{
    /// <summary>
    /// PERFIL DE AUTOMAPPER: define cómo se convierte cada entidad de dominio
    /// en sus DTOs/ViewModels de salida.
    ///
    /// ¿POR QUÉ CENTRALIZAR EL MAPEO?
    /// - Un solo lugar donde ver TODAS las conversiones Entity → DTO.
    /// - Los mapeos "especiales" quedan explícitos: aplanar Value Objects
    ///   (Potencia.Valor → PotenciaKW), enums a string, campos calculados.
    /// - AddAutoMapper() descubre este Profile automáticamente al arrancar y
    ///   AssertConfigurationIsValid puede verificar que no falta ningún campo.
    /// </summary>
    public sealed class ProyectoMappingProfile : Profile
    {
        public ProyectoMappingProfile()
        {
            // ── ProyectoRenovable → DTOs de commands ────────────────────
            CreateMap<ProyectoRenovable, CrearProyectoResponse>()
                .ForMember(d => d.Tipo, o => o.MapFrom(s => s.Tipo.ToString()))
                .ForMember(d => d.Estado, o => o.MapFrom(s => s.Estado.ToString()))
                // APLANADO de Value Object: el DTO expone el decimal crudo.
                .ForMember(d => d.PotenciaKW, o => o.MapFrom(s => s.Potencia.Valor));

            CreateMap<ProyectoRenovable, ActualizarProyectoResponse>()
                .ForMember(d => d.Tipo, o => o.MapFrom(s => s.Tipo.ToString()))
                .ForMember(d => d.Estado, o => o.MapFrom(s => s.Estado.ToString()))
                .ForMember(d => d.PotenciaKW, o => o.MapFrom(s => s.Potencia.Valor))
                .ForMember(d => d.Latitud, o => o.MapFrom(s => s.Ubicacion.Latitud))
                .ForMember(d => d.Longitud, o => o.MapFrom(s => s.Ubicacion.Longitud));

            CreateMap<ProyectoRenovable, ActivarProyectoResponse>()
                .ForMember(d => d.Estado, o => o.MapFrom(s => s.Estado.ToString()));

            // ── ProyectoRenovable → ViewModels de queries ───────────────
            CreateMap<ProyectoRenovable, ProyectoListadoViewModel>()
                .ForMember(d => d.Tipo, o => o.MapFrom(s => s.Tipo.ToString()))
                .ForMember(d => d.Estado, o => o.MapFrom(s => s.Estado.ToString()))
                .ForMember(d => d.PotenciaKW, o => o.MapFrom(s => s.Potencia.Valor))
                .ForMember(d => d.Latitud, o => o.MapFrom(s => s.Ubicacion.Latitud))
                .ForMember(d => d.Longitud, o => o.MapFrom(s => s.Ubicacion.Longitud));

            CreateMap<ProyectoRenovable, ProyectoDetalleViewModel>()
                .ForMember(d => d.Tipo, o => o.MapFrom(s => s.Tipo.ToString()))
                .ForMember(d => d.Estado, o => o.MapFrom(s => s.Estado.ToString()))
                .ForMember(d => d.PotenciaKW, o => o.MapFrom(s => s.Potencia.Valor))
                .ForMember(d => d.Latitud, o => o.MapFrom(s => s.Ubicacion.Latitud))
                .ForMember(d => d.Longitud, o => o.MapFrom(s => s.Ubicacion.Longitud))
                // CAMPO CALCULADO: no existe en BD, se computa al mapear.
                .ForMember(d => d.TotalEnergiaGeneradaKWh,
                    o => o.MapFrom(s => s.Mediciones.Sum(m => m.EnergiaGeneradaKWh)));

            // ── Entidades hijas ─────────────────────────────────────────
            CreateMap<Medicion, MedicionViewModel>();

            CreateMap<Medicion, RegistrarMedicionResponse>()
                .ForMember(d => d.ProyectoId, o => o.MapFrom(s => s.ProyectoRenovableId));

            CreateMap<Instalacion, InstalacionViewModel>()
                .ForMember(d => d.PotenciaKW, o => o.MapFrom(s => s.PotenciaInstalada.Valor));
        }
    }
}
