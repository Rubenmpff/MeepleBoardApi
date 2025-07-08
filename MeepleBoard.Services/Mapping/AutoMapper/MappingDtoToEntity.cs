using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Services.DTOs;

namespace MeepleBoardApi.Services.Mapping.AutoMapper
{
    public class MappingDtoToEntity : Profile
    {
        public MappingDtoToEntity()
        {
            CreateMap<GameDto, Game>()
                .ForMember(dest => dest.Id, opt => opt.Ignore()) // ID gerado pelo banco
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ReverseMap(); // 🔹 Permite conversão Game <-> GameDTO

            CreateMap<MatchDto, Match>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ReverseMap(); // 🔹 Permite conversão Match <-> MatchDTO

            CreateMap<UserDto, User>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ReverseMap(); // 🔹 Permite conversão User <-> UserDTO

            CreateMap<UserGameLibraryDto, UserGameLibrary>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ReverseMap(); // 🔹 Permite conversão UserGameLibrary <-> UserGameLibraryDTO
        }
    }
}