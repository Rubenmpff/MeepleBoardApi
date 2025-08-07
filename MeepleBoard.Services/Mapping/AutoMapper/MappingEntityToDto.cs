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
                .ForMember(dest => dest.GameName, opt => opt.MapFrom(src => src.Game != null ? src.Game.Name : "Jogo Desconhecido"))
                .ForMember(dest => dest.WinnerName, opt => opt.MapFrom(src => src.Winner != null ? src.Winner.UserName : "Sem Vencedor"));

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
                .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.User != null ? src.User.UserName : "Jogador Desconhecido"));

            // --- GameSession ---
            CreateMap<GameSession, GameSessionDto>()
                .ForMember(dest => dest.Players, opt => opt.MapFrom(src => src.Players));
        }
    }
}
