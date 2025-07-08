using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Interfaces
{
    public interface IUserService
    {
        Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        Task<UserDto?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

        Task<IEnumerable<UserDto>> GetAllAsync(CancellationToken cancellationToken = default);

        Task AddAsync(UserDto userDto, CancellationToken cancellationToken = default);

        Task UpdateAsync(UserDto userDto, CancellationToken cancellationToken = default);

        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

        Task<UserDto> GetUserStatisticsAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}