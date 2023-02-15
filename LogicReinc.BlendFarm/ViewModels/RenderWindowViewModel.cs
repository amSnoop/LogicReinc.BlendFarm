
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Client.Tasks;
using LogicReinc.BlendFarm.Objects;
using LogicReinc.BlendFarm.Server;
using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Windows;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.ViewModels
{
    public partial class RenderWindowViewModel : ObservableObject
    {


        #region Observable Properties

        [ObservableProperty]
        private bool _isLiveChanging = false, _isQueuing = false;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CurrentSessionID))]
        private OpenBlenderProject _currentProject;
        [ObservableProperty]
        private string _queueNameProperty = "";

        #endregion



        #region Public Properties
        public RenderWindowNew WindowNew { get; set; }
        public BlenderVersion Version { get; set; }
        public ObservableCollection<OpenBlenderProject> Projects { get; set; } = new ObservableCollection<OpenBlenderProject>();
        public ObservableCollection<QueueItem> Queue { get; set; } = new ObservableCollection<QueueItem>();
        public ObservableCollection<RenderNode> Nodes { get; private set; } = new ObservableCollection<RenderNode>();

        public bool IsClientConnecting, UseAutomaticPerformance, UseSyncCompression;

        public string NewClientName, NewClientAddress, OS;
        public bool IsWindows => OS == SystemInfo.OS_WINDOWS64;
        public bool IsLinux => OS == SystemInfo.OS_LINUX64;
        public bool IsMacOS => OS == SystemInfo.OS_MACOS;
        public BlendFarmManager Manager { get; set; } = null;
        public RenderTask CurrentTask = null;
        public bool IsRendering => CurrentTask != null;
        public string CurrentSessionID => CurrentProject?.SessionID;

        #endregion

        #region Protected Properties

        protected string[] DenoiserOptions { get; } = new string[] { "Inherit", "None", "NLM", "Optix", "OpenImage Denoise" };
        protected EngineType[] EngineOptions { get; } = (EngineType[])Enum.GetValues(typeof(EngineType));

        protected string[] ImageFormats { get; } = Client.ImageTypes.ImageFormats.Formats;

        #endregion

        #region Private Properties

        private int queueCount = 0;
        private Thread queueThread = null;
        private string lastAnimationDirectory = null;

        private Image image = null;
        private ProgressBar imageProgress = null;
        private TextBlock lastRenderTime = null;
        private ComboBox selectStrategy = null;
        private ComboBox selectOrder = null;
        private ComboBox selectOutputType = null;
        private AutoCompleteBox scenesAvailableBox = null;
        private ListBox renderType = null;

        #endregion

        #region Debug

        private ObservableCollection<RenderNode> _testNodes = new ObservableCollection<RenderNode>(new List<RenderNode>()
        {
            new RenderNode()
            {
                Name = "Local",
                Address = "Localhost"
            },
            new RenderNode()
            {
                Name = "WhateverPC",
                Address = "192.168.1.212"
            }
        });

        RenderWindowViewModel()
        {
            Projects = new ObservableCollection<OpenBlenderProject>()
            {
                new OpenBlenderProject("C://some/blend/dir/Example Project.blend"){
                    UseNetworkedPath = true
                    },
                new OpenBlenderProject("C://some/blend/dir/Some other project.blend"),
                new OpenBlenderProject("C://some/blend/dir/asdf1234.blend"),
                new OpenBlenderProject("C://some/blend/dir/testing.blend"),
            };
            Queue = new ObservableCollection<QueueItem>()
            {
                new QueueItem(this, new OpenBlenderProject("C://whatever/testproject.blend"), new RenderManagerSettings()
                {

                }){
                        Task = new ChunkedTask(null, null, null, 0)
                        {
                            Progress = 0.43
                        }
                },
                new QueueItem(this, new OpenBlenderProject("C://whatever/asdfdsag.blend"), new RenderManagerSettings()
                {

                })
            };
            //File = "path/to/some/blendfile.blend";
            CurrentProject = LoadProject("path/to/some/blendfile.blend");
            Version = new Shared.BlenderVersion()
            {
                Name = "blender-2.9.2"
            };
            Init();
        }



        #endregion

        public RenderWindowViewModel(BlendFarmManager manager, BlenderVersion version, string blenderFile)
        {
            Manager = manager;
            CurrentProject = LoadProject(blenderFile);
            Version = version;

            Init();
        }
        private void Init()
        {
            OS = SystemInfo.GetOSName();
            if (Manager?.Nodes != null)
            {
                foreach (RenderNode node in Manager.Nodes.ToList())
                    Nodes.Add(node);
                Manager.OnNodeAdded += (manager, node) => Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Nodes.Add(node);
                });
                Manager.OnNodeRemoved += (manager, node) => Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Nodes.Remove(node);
                });
            }
            else
                Nodes = _testNodes;
            Manager?.StartFileWatch();

            WindowNew = new RenderWindowNew()
            {
                DataContext = this
            };

            WindowNew.Closed += (a, b) =>
            {
                LocalServer.Stop();
                Manager.StopFileWatch();
                Manager.Cleanup();
            };
        }
        public OpenBlenderProject LoadProject(string blendFile)
        {
            string sessionID = Manager.GetFileSessionID(blendFile);
            OpenBlenderProject proj = new OpenBlenderProject(blendFile, sessionID);
            proj.OnBitmapChanged += async (proj, bitmap) =>
            {

                if (proj == CurrentProject)
                    await Dispatcher.UIThread.InvokeAsync(() => image.Source = bitmap); ;
            };
            proj.OnNetworkedChanged += async (proj, networked) =>
            {
                Manager.IsNetworked = networked;
                foreach (var node in Nodes.Where(x => x.Connected))
                    node.UpdateSyncedStatus(proj.SessionID, false);
            };
            Projects.Add(proj);

            SwitchProject(proj);

            return proj;
        }
        public async Task SwitchProject(OpenBlenderProject proj)
        {
            CurrentProject = proj;
            Manager.SetSelectedSessionID(CurrentProject.SessionID);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                image.Source = proj.LastImage;
                scenesAvailableBox.Items = CurrentProject.ScenesAvailable;
            });
        }
        public async void ConnectAll()
        {
            try
            {
                await Manager.ConnectAndPrepareAll();
            }
            catch { }
        }
        public async Task SyncAll()//this logic should be handled by the manager, and it should probably just be passed a reference to the current project.
        {
            if (!CurrentProject.UseNetworkedPath)
                await Manager?.Sync(CurrentProject.BlendFile, UseSyncCompression);
            else
                await Manager?.Sync(CurrentProject.BlendFile, CurrentProject.NetworkPathWindows, CurrentProject.NetworkPathLinux, CurrentProject.NetworkPathMacOS);
        }

        public void AddNewNode()
        {
            if (!string.IsNullOrEmpty(NewClientAddress) && !string.IsNullOrEmpty(NewClientName))
            {
                if (!Regex.IsMatch(NewClientAddress, "^([a-zA-Z0-9\\.]*?):[0-9][0-9]?[0-9]?[0-9]?[0-9]?$"))
                {
                    MessageWindow.Show(this.WindowNew, "Invalid Address", "The address provided seems to be invalid, expected format is {hostname}:{port} or {ip}:{port}, eg. 192.168.1.123:15000");
                    return;
                }

            }
            Manager.AddNode(NewClientName, NewClientAddress);

            BlendFarmSettings.Instance.PastClients.Add(NewClientName, new BlendFarmSettings.HistoryClient()
            {
                Address = NewClientAddress,
                Name = NewClientName
            });
            BlendFarmSettings.Instance.Save();
        }
        public async Task OpenProjectDialog()
        {
            OpenFileDialog dialog = new OpenFileDialog()
            {
                Title = "Select a Blendfile",
                Filters = new List<FileDialogFilter>()
                {
                    new FileDialogFilter()
                    {
                        Name = "Blender File (.blend)",
                        Extensions = new List<string>()
                        {
                            "blend"
                        }
                    }
                }
            };

            string[] paths = await dialog.ShowAsync(this.WindowNew);
            paths = paths?.Select(x => Statics.SanitizePath(x)).ToArray();

            if (paths != null)
                foreach (string path in paths)
                {
                    if (!File.Exists(path))
                        await MessageWindow.Show(this.WindowNew, "Invalid Path", $"Path {path} does not exist.");
                    else
                        LoadProject(path);
                }
        }
    }
}
