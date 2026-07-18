using Pinder.Core.Conversation;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Provides access to base response timing profiles loaded from timing data.
    /// </summary>
    public interface ITimingRepository
    {
        /// <summary>Returns the timing profile with the given id, or null if not found.</summary>
        TimingProfile? GetProfile(string profileId);
    }
}
