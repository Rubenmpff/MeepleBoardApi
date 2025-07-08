using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Services.DTOs;

namespace MeepleBoardApi.Services.Mapping.AutoMapper
{
    public class MappingEntityToDto : Profile
    {
        public MappingEntityToDto()
        {
            CreateMap<Match, MatchDto>()
                .ForMember(dest => dest.GameName, opt => opt.MapFrom(src => src.Game != null ? src.Game.Name : "Jogo Desconhecido")) // 🔹 Evita erro se `Game` for null
                .ForMember(dest => dest.WinnerName, opt => opt.MapFrom(src => src.Winner != null ? src.Winner.UserName : "Sem Vencedor")); // 🔹 Evita erro se não houver vencedor

            CreateMap<User, UserDto>();

            CreateMap<UserGameLibrary, UserGameLibraryDto>()
                .ForMember(dest => dest.GameName, opt => opt.MapFrom(src => src.Game != null ? src.Game.Name : "Jogo Desconhecido")) // 🔹 Evita erro se `Game` for null
                .ForMember(dest => dest.GameImageUrl, opt => opt.MapFrom(src => src.Game != null ? src.Game.ImageUrl : null)); // 🔹 Evita erro se `Game` for null

            CreateMap<Game, GameDto>()
                .ForMember(dest => dest.IsExpansion, opt => opt.MapFrom(src => src.BaseGameId.HasValue));
        }
    }
}