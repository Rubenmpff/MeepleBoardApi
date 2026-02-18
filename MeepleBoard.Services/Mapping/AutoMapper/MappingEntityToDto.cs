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
            // --- MatchPlayer ---
            CreateMap<MatchPlayer, MatchPlayerDto>()
                .ForMember(d => d.MatchId, o => o.MapFrom(s => s.MatchId))
                .ForMember(d => d.UserId, o => o.MapFrom(s => s.UserId))
                .ForMember(d => d.UserName, o => o.MapFrom(s => s.User != null ? s.User.UserName : "Jogador Desconhecido"))
                .ForMember(d => d.Score, o => o.MapFrom(s => s.Score))
                .ForMember(d => d.IsWinner, o => o.MapFrom(s => s.IsWinner))
                .ForMember(d => d.RankPosition, o => o.MapFrom(s => s.RankPosition));

            // --- Match ---
            CreateMap<Match, MatchDto>()
                .ForMember(d => d.GameName, o => o.MapFrom(s => s.Game != null ? s.Game.Name : "Jogo Desconhecido"))
                .ForMember(d => d.WinnerName, o => o.MapFrom(s => s.Winner != null ? s.Winner.UserName : "Sem Vencedor"))
                .ForMember(d => d.Players, o => o.MapFrom(s => s.MatchPlayers));

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
                .ForMember(d => d.OrganizerId, opt => opt.MapFrom(s => s.OrganizerId))
                .ForMember(d => d.OrganizerUserName, opt => opt.MapFrom(s => s.Organizer != null ? s.Organizer.UserName : string.Empty))
                .ForMember(d => d.Players, opt => opt.MapFrom(s => s.Players))
                .ForMember(d => d.Matches, opt => opt.MapFrom(s => s.Matches));
        }
    }
}
