using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeepleBoard.Services.Mapping.Dtos
{
    public record FriendLiteDto(Guid id, string userName);

    public record FriendRequestDto(Guid requestId, Guid fromUserId, string fromUserName, DateTime createdAt);

}
