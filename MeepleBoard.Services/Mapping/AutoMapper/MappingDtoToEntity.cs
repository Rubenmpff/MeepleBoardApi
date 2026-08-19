using AutoMapper;
using MeepleBoard.Application.DTOs;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Services.DTOs;

namespace MeepleBoardApi.Services.Mapping.AutoMapper
{
    public class MappingDtoToEntity : Profile
    {
        public MappingDtoToEntity()
        {
            // --- Game ---
            CreateMap<GameDto, Game>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ReverseMap();

            // --- User ---
            CreateMap<UserDto, User>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ReverseMap();

            // --- UserGameLibrary ---
            CreateMap<UserGameLibraryDto, UserGameLibrary>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ReverseMap();

            // --- GameSession ---
            // Nota: GameSession usa construtor com parâmetros — o AutoMapper
            // NÃO cria a entidade diretamente. O GameSessionService faz isso
            // manualmente via construtor. Este mapeamento é removido para evitar
            // erros com propriedades calculadas (IsActive, Status, etc.).
            // Se precisares de mapear CreateGameSessionDto → GameSession
            // no futuro, usa ConstructUsing().

            // --- GameSessionPlayer ---
            CreateMap<GameSessionPlayerDto, GameSessionPlayer>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.JoinedAt, opt => opt.Ignore())
                .ForMember(dest => dest.LeftAt, opt => opt.Ignore());
        }
    }
}