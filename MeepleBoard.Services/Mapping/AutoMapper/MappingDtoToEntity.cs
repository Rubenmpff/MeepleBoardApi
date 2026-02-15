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

            // --- Match ---
            CreateMap<MatchDto, Match>()
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

            // --- GameSession (para criar) ---
            CreateMap<CreateGameSessionDto, GameSession>()
                .ForMember(d => d.Id, o => o.Ignore())
                .ForMember(d => d.IsActive, o => o.Ignore())
                .ForMember(d => d.StartDate, o => o.Ignore())
                .ForMember(d => d.EndDate, o => o.Ignore());

            // --- GameSessionPlayer ---
            CreateMap<GameSessionPlayerDto, GameSessionPlayer>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.JoinedAt, opt => opt.Ignore())
                .ForMember(dest => dest.LeftAt, opt => opt.Ignore());
        }
    }
}
