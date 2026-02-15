// MeepleBoard.Services/Interfaces/IGameSessionService.cs
using MeepleBoard.Application.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MeepleBoard.Services.Interfaces
{
    public interface IGameSessionService
    {
        Task<IEnumerable<GameSessionDto>> GetAllAsync(bool includeRelations = false);
        Task<GameSessionDto?> GetByIdAsync(Guid id, bool includeRelations = true);
        Task<GameSessionDto> CreateAsync(string name, Guid organizerId, string? location = null);
        Task AddPlayerAsync(Guid sessionId, Guid userId, bool isOrganizer = false);
        Task RemovePlayerAsync(Guid sessionId, Guid userId);
        Task CloseSessionAsync(Guid sessionId);
    }
}
