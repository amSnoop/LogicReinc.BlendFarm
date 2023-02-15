using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Objects;
using LogicReinc.BlendFarm.Server;
using LogicReinc.BlendFarm.Shared;

namespace LogicReinc.BlendFarm
{
    public class ClientManager
    {
        public OpenBlenderProject CurrentProject { get; set; }
        public BlenderVersion Version { get; set; }
        public string OS { get; set; }
        public bool IsWindows => OS == SystemInfo.OS_WINDOWS64;
        public bool IsLinux => OS == SystemInfo.OS_LINUX64;
        public bool IsMacOS => OS == SystemInfo.OS_MACOS;
        public bool IsIdle => CurrentProject.CurrentTask == null;
        public bool IsNetworkPath { get; set; } = false;
        /// <summary>
        /// If != "" Then the ViewModel will show a message window that contains the string 
        /// </summary>
        public string ErrorState { get; set; } = "";
        /// <summary>
        /// Displays the name of the current error, program will check for ErrorState != "" before looking for a name.
        /// </summary>
        public string ErrorName { get; set; } = "";

        public ClientManager(OpenBlenderProject curProj, BlenderVersion vers) { 
            CurrentProject = curProj;
            Version = vers;
            OS = SystemInfo.GetOSName();
        }
    }
}
