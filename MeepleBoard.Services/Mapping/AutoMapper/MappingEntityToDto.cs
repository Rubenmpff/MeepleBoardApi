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
                .ForMember(d => d.WinnerName, o => o.MapFrom(s => s.Winner != null ? s.Winner.UserName : null))
                .ForMember(d => d.Players, o => o.MapFrom(s => s.MatchPlayers))
                // ── Diário de partida ────────────────────────────────────────
                .ForMember(d => d.PersonalRating, o => o.MapFrom(s => s.PersonalRating))
                .ForMember(d => d.Notes, o => o.MapFrom(s => s.Notes))
                .ForMember(d => d.Tags, o => o.MapFrom(s => s.Tags))
                // ── Estado do diário ─────────────────────────────────────────
                .ForMember(d => d.JournalStatus, o => o.MapFrom(s => s.JournalStatus.ToString()))
                .ForMember(d => d.ClosedAt, o => o.MapFrom(s => s.ClosedAt))
                // ── Modo oficial ─────────────────────────────────────────────
                .ForMember(d => d.IsOfficialMode, o => o.MapFrom(s => s.IsOfficialMode))
                .ForMember(d => d.UnofficialModeJustification, o => o.MapFrom(s => s.UnofficialModeJustification));

            // --- User ---
            CreateMap<User, UserDto>();

            // --- UserGameLibrary ---
            CreateMap<UserGameLibrary, UserGameLibraryDto>()
                .ForMember(dest => dest.GameName, opt => opt.MapFrom(src => src.Game != null ? src.Game.Name : "Jogo Desconhecido"))
                .ForMember(dest => dest.GameImageUrl, opt => opt.MapFrom(src => src.Game != null ? src.Game.ImageUrl : null))
                .ForMember(dest => dest.GameId, opt => opt.MapFrom(src => src.GameId))
                .ForMember(dest => dest.BggId, opt => opt.MapFrom(src => src.Game != null ? src.Game.BGGId : null))
                .ForMember(dest => dest.AverageRating, opt => opt.MapFrom(src => src.Game != null ? src.Game.AverageRating : null))
                .ForMember(dest => dest.MinPlayers, opt => opt.MapFrom(src => src.Game != null ? src.Game.MinPlayers : null))
                .ForMember(dest => dest.MaxPlayers, opt => opt.MapFrom(src => src.Game != null ? src.Game.MaxPlayers : null))
                .ForMember(dest => dest.IsExpansion, opt => opt.MapFrom(src => src.Game != null && src.Game.IsExpansion))
                .ForMember(dest => dest.IsCooperative, opt => opt.MapFrom(src => src.Game != null && src.Game.IsCooperative))
                .ForMember(dest => dest.SupportsSoloMode, opt => opt.MapFrom(src => src.Game != null && src.Game.SupportsSoloMode));

            // --- Game ---
            CreateMap<Game, GameDto>()
                .MaxDepth(3) // ⚠️ Game↔Expansions é auto-referencial — limite explícito contra GHSA-rvv3-g6hj-g44x (DoS por recursão descontrolada no AutoMapper)
                .ForMember(dest => dest.IsExpansion, opt => opt.MapFrom(src => src.BaseGameId.HasValue))
                .ForMember(dest => dest.MinPlayers, opt => opt.MapFrom(src => src.MinPlayers))
                .ForMember(dest => dest.MaxPlayers, opt => opt.MapFrom(src => src.MaxPlayers))
                .ForMember(dest => dest.IsCooperative, opt => opt.MapFrom(src => src.IsCooperative))
                .ForMember(dest => dest.SupportsSoloMode, opt => opt.MapFrom(src => src.SupportsSoloMode));

            // --- GameSessionPlayer ---
            CreateMap<GameSessionPlayer, GameSessionPlayerDto>()
                .ForMember(d => d.UserId, o => o.MapFrom(s => s.UserId))
                .ForMember(d => d.UserName, o => o.MapFrom(s => s.User != null ? s.User.UserName : "Jogador Desconhecido"))
                .ForMember(d => d.IsOrganizer, o => o.MapFrom(s => s.IsOrganizer))
                .ForMember(d => d.Status, o => o.MapFrom(s => s.Status))
                .ForMember(d => d.InvitedAt, o => o.MapFrom(s => s.InvitedAt))
                .ForMember(d => d.RespondedAt, o => o.MapFrom(s => s.RespondedAt))
                .ForMember(d => d.JoinedAt, o => o.MapFrom(s => s.JoinedAt))
                .ForMember(d => d.LeftAt, o => o.MapFrom(s => s.LeftAt));

            // --- GameSession ---
            CreateMap<GameSession, GameSessionDto>()
                .ForMember(d => d.OrganizerId, opt => opt.MapFrom(s => s.OrganizerId))
                .ForMember(d => d.OrganizerUserName, opt => opt.MapFrom(s => s.Organizer != null ? s.Organizer.UserName : string.Empty))
                .ForMember(d => d.ScheduledStartDate, opt => opt.MapFrom(s => s.ScheduledStartDate))
                .ForMember(d => d.StartDate, opt => opt.MapFrom(s => s.StartDate))
                .ForMember(d => d.EndDate, opt => opt.MapFrom(s => s.EndDate))
                .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status))
                .ForMember(d => d.Players, opt => opt.MapFrom(s => s.Players))
                .ForMember(d => d.Matches, opt => opt.MapFrom(s => s.Matches));
        }
    }
}