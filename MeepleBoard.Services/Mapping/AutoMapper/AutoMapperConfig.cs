using AutoMapper;

namespace MeepleBoard.Services.Mapping.AutoMapper
{
    public class AutoMapperConfig : Profile
    {
        public static MapperConfiguration RegisterMappings()
        {
            return new MapperConfiguration(profiles =>
            {
                profiles.AddProfile(new MappingDtoToEntity());
                profiles.AddProfile(new MappingEntityToDto());
            });
        }
    }
}