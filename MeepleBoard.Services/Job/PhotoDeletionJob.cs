using Hangfire;
using MeepleBoard.Services.ExternalServices.Interfaces;

namespace MeepleBoard.Services.Job
{
    /// <summary>
    /// Removes one photo from storage when the immediate attempts after an account deletion failed.
    /// The argument is only the storage reference (no identity). Hangfire retries it; DeviceTests records it.
    /// </summary>
    public class PhotoDeletionJob(IPhotoStorageService storage)
    {
        [AutomaticRetry(Attempts = 10)]
        public Task ExecuteAsync(string photoUrl) => storage.DeleteAsync(photoUrl);
    }
}
