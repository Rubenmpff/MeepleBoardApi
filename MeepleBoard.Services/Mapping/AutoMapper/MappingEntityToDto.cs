using AutoMapper;
using MeepleBoard.Application.DTOs;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Services.DTOs;

namespace MeepleBoardApi.Services.Mapping.AutoMapper
{
    public class MappingEntityToDto : Profile
    {
        public MappingEntityToDto()
        {
            // --- Match ---
            CreateMap<Match, MatchDto>()
                .ForMember(d => d.GameName, o => o.MapFrom(s => s.Game != null ? s.Game.Name : "Jogo Desconhecido"))
                .ForMember(d => d.WinnerName, o => o.MapFrom(s => s.Winner != null ? s.Winner.UserName : "Sem Vencedor"));

            // --- User ---
            CreateMap<User, UserDto>();

            // --- UserGameLibrary ---
            CreateMap<UserGameLibrary, UserGameLibraryDto>()
                .ForMember(dest => dest.GameName, opt => opt.MapFrom(src => src.Game != null ? src.Game.Name : "Jogo Desconhecido"))
                .ForMember(dest => dest.GameImageUrl, opt => opt.MapFrom(src => src.Game != null ? src.Game.ImageUrl : null))
                .ForMember(dest => dest.GameId, opt => opt.MapFrom(src => src.GameId))
                .ForMember(dest => dest.BggId, opt => opt.MapFrom(src => src.Game != null ? src.Game.BGGId : null));

            // --- Game ---
            CreateMap<Game, GameDto>()
                .ForMember(dest => dest.IsExpansion, opt => opt.MapFrom(src => src.BaseGameId.HasValue));

            // --- GameSessionPlayer ---
            CreateMap<GameSessionPlayer, GameSessionPlayerDto>()
                .ForMember(d => d.UserName, o => o.MapFrom(s => s.User != null ? s.User.UserName : "Jogador Desconhecido"));

            // --- GameSession ---
            CreateMap<GameSession, GameSessionDto>()
                .ForMember(d => d.Organizer, o => o.MapFrom(s => s.OrganizerId.ToString()))
                .ForMember(d => d.Players, o => o.MapFrom(s => s.Players))
                .ForMember(d => d.Matches, o => o.MapFrom(s => s.Matches));
        }
    }
}
