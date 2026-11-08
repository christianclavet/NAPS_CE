using System;
using System.Collections.Generic;
using NAPS2.Scan;

namespace NAPS2.Config
{
    /// <summary>
    /// Stores the single scanning profile used by the NAPS2 scanning server (scanning server mode).
    /// Connected clients never choose the profile: they read it from the server.
    /// It is kept in its own file, separate from the user's profiles.xml.
    /// </summary>
    public class ServerProfileManager : ConfigManager<List<ScanProfile>>
    {
        public ServerProfileManager()
            : base("server-profile.xml", Paths.AppData, null, () => new List<ScanProfile> { new ScanProfile() })
        {
        }

        /// <summary>
        /// The profile the server uses for every client request.
        /// </summary>
        public ScanProfile Profile
        {
            get
            {
                if (Config.Count == 0)
                {
                    Config.Add(new ScanProfile());
                }
                return Config[0];
            }
        }

        /// <summary>
        /// Replaces the server profile with the given one and saves it to disk.
        /// </summary>
        public void SetProfile(ScanProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            Config.Clear();
            Config.Add(profile);
            Save();
        }
    }
}
