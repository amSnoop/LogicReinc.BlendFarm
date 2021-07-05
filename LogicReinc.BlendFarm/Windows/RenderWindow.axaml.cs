﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Server;
using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Image = Avalonia.Controls.Image;

namespace LogicReinc.BlendFarm.Windows
{
    public class RenderWindow : Window
    {
        private static DirectProperty<RenderWindow, bool> IsRenderingProperty =
            AvaloniaProperty.RegisterDirect<RenderWindow, bool>(nameof(IsRendering), (x) => x.IsRendering);
        private static DirectProperty<RenderWindow, bool> IsLiveChangingProperty =
            AvaloniaProperty.RegisterDirect<RenderWindow, bool>(nameof(IsLiveChanging), (x) => x.IsLiveChanging);
        private static DirectProperty<RenderWindow, bool> UseFPSProperty =
            AvaloniaProperty.RegisterDirect<RenderWindow, bool>(nameof(UseFPS), (x) => x.UseFPS, (w, v) => w.UseFPS = v);

        public string File { get; set; }
        public BlenderVersion Version { get; set; }

        public bool IsClientConnecting { get; set; }
        public string InputClientName { get; set; }
        public string InputClientAddress { get; set; }

        //Render Properties
        public int RenderWidth { get; set; } = 1280;
        public int RenderHeight { get; set; } = 720;
        public int ChunkSize { get; set; } = 256;
        public int Samples { get; set; } = 32;
        public string Denoiser { get; set; } = "Inherit";

        public bool UseWorkaround { get; set; } = true;
        public bool UseAutomaticPerformance { get; set; } = true;
        public bool UseSyncCompression { get; set; } = false;

        public string AnimationFileFormat { get; set; } = "#.png";
        public int FrameStart { get; set; } = 0;
        public int FrameEnd { get; set; } = 60;
        public int FPS { get; set; } = 0;
        private bool _useFPS = false;
        public bool UseFPS
        {
            get => _useFPS;
            set
            {
                bool old = _useFPS;
                _useFPS = value;
                RaisePropertyChanged(UseFPSProperty, old, value);
            }
        }

        //State
        public bool IsLiveChanging { get; set; } = false;

        public ObservableCollection<RenderNode> Nodes { get; private set; } = new ObservableCollection<RenderNode>();
        public BlendFarmManager Manager { get; set; } = null;

        public bool IsRendering => CurrentTask != null;
        public RenderTask CurrentTask = null;

        //Options
        protected string[] DenoiserOptions { get; } = new string[] { "Inherit", "None", "NLM", "OPTIX", "OPENIMAGEDENOISE" };

        //Dialogs
        private string _lastAnimationDirectory = null;


        //Views
        private ListBox _nodeList = null;
        private Image _image = null;
        private ProgressBar _imageProgress = null;
        private TextBlock _lastRenderTime = null;
        private ComboBox _selectStrategy = null;
        private ComboBox _selectOrder = null;

        private Bitmap _lastBitmap = null;

        //Debug data
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



        public RenderWindow()
        {
            File = "path/to/some/blendfile.blend";
            Version = new Shared.BlenderVersion()
            {
                Name = "blender-2.9.2"
            };
            Init();
        }
        public RenderWindow(BlendFarmManager manager, BlenderVersion version, string blenderFile, string sessionID = null)
        {
            Manager = manager;
            File = blenderFile;
            Version = version;

            using (Stream icoStream = Program.GetIconStream())
            {
                this.Icon = new WindowIcon(icoStream);
            }

            Init();
        }
        private void Init()
        {
            if(Manager?.Nodes != null)
            {
                foreach(RenderNode node in Manager.Nodes.ToList())
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
                Nodes =  _testNodes;
            DataContext = this;

            this.Closed += (a, b) =>
            {
                LocalServer.Stop();
                Manager.StopFileWatch();
            };
            Manager?.StartFileWatch();

            this.InitializeComponent();
#if DEBUG
            //this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            MinHeight = 600;
            MinWidth = 500;
            Width = 1400;
            Height = 950;

            _nodeList = this.Find<ListBox>("listNodes");
            _image = this.Find<Image>("render");
            _imageProgress = this.Find<ProgressBar>("renderProgress");
            _lastRenderTime = this.Find<TextBlock>("lastRenderTime");
            _selectStrategy = this.Find<ComboBox>("selectStrategy");
            _selectOrder = this.Find<ComboBox>("selectOrder");

            _selectStrategy.Items = Enum.GetValues(typeof(RenderStrategy));
            _selectStrategy.SelectedIndex = 0;
            _selectOrder.Items = Enum.GetValues(typeof(TaskOrder));
            _selectOrder.SelectedIndex = 0;

            _image.KeyDown += async (a, b) =>
            {
                if (b.Key == Avalonia.Input.Key.Delete)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _lastBitmap = FromDrawingBitmap(new System.Drawing.Bitmap(1, 1));
                        _image.Source = _lastBitmap;
                        _lastRenderTime.Text = "";
                    });
                }
            };
        }

        public async void ConnectAll()
        {
            try
            {
                await Manager.ConnectAndPrepareAll();
            }
            catch { }
        }
        public async Task SyncAll()
        {
            await Manager?.Sync(UseSyncCompression);
        }

        public void AddNewNode()
        {
            if (!string.IsNullOrEmpty(InputClientAddress) && !string.IsNullOrEmpty(InputClientName))
            {
                if (BlendFarmSettings.Instance.PastClients.Any(x => x.Key == InputClientName || x.Value.Address == InputClientAddress))
                {
                    MessageWindow.Show(this, "Node already exists", "Node already exists, use a different name and address");
                    return;
                }
                if(!Regex.IsMatch(InputClientAddress, "^([a-zA-Z0-9\\.]*?):[0-9][0-9]?[0-9]?[0-9]?[0-9]?$"))
                {
                    MessageWindow.Show(this, "Invalid Address", "The address provided seems to be invalid, expected format is {hostname}:{port} or {ip}{port}, eg. 192.168.1.123:15000");
                    return;
                }

                Manager.AddNode(InputClientName, InputClientAddress);

                BlendFarmSettings.Instance.PastClients.Add(InputClientName, new BlendFarmSettings.HistoryClient()
                {
                    Address = InputClientAddress,
                    Name = InputClientName,
                    RenderType = RenderType.CPU
                });
                BlendFarmSettings.Instance.Save();
            }
            else
                MessageWindow.Show(this, "No name or address", "A node requires both a name and an address");
        }

        public void DeleteNode(RenderNode node)
        {
            Manager.RemoveNode(node.Name);

            var nodeEntry = BlendFarmSettings.Instance.PastClients.FirstOrDefault(x => x.Key == node.Name).Key;
            if (nodeEntry != null)
            {
                BlendFarmSettings.Instance.PastClients.Remove(nodeEntry);
                BlendFarmSettings.Instance.Save();
            }
        }
        public async void ConfigureNode(RenderNode node)
        {
            DeviceSettingsWindow.Show(this, node);
        }

        public async Task Render() => await Render(false, false);
        public async Task Render(bool noSync, bool noExcep = false)
        {
            if (CurrentTask != null)
                return;

            //Show Progressbar
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                this._imageProgress.IsVisible = true;
                this._imageProgress.IsIndeterminate = true;
            });

            //Check if any unsynced nodes
            if(!noSync && Manager.Nodes.Any(x=> x.Connected && !x.IsSynced))
            {
                if(await YesNoNeverWindow.Show(this, "Unsynced nodes", "You have nodes that are not yet synced, would you like to sync them to use for rendering?", "syncBeforeRendering"))
                    await Manager.Sync(UseSyncCompression);
            }

            //Start rendering thread
            await Task.Run(async () =>
            {
                try
                {
                    Stopwatch watch = new Stopwatch();
                    watch.Start();


                    //Create Task
                    CurrentTask = Manager.GetRenderTask(GetSettingsFromUI(), async (task, updated) =>
                    {
                        //Apply image to canvas
                        await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                _lastBitmap = FromDrawingBitmap(updated);
                                _image.Source = _lastBitmap;
                                _lastRenderTime.Text = watch.Elapsed.ToString();
                            });
                    });

                    //Progress Updating
                    CurrentTask.OnProgress += async (task, progress) =>
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            this._imageProgress.IsIndeterminate = false;
                            this._imageProgress.Value = progress * 100;
                        });
                    };

                    //Update view
                    await Dispatcher.UIThread.InvokeAsync(() => RaisePropertyChanged(IsRenderingProperty, false, true));

                    //Render
                    var finalBitmap = await CurrentTask.Render();

                    //Finalize
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (finalBitmap != null)
                        {
                            _lastBitmap = FromDrawingBitmap(finalBitmap);
                            _image.Source = _lastBitmap;
                            finalBitmap.Save("lastRender.png");
                        }
                        _lastRenderTime.Text = watch.Elapsed.ToString();
                        this._imageProgress.IsVisible = false;
                    });
                    watch.Stop();

                }
                catch (Exception ex)
                {
                    if(!noExcep)
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            MessageWindow.Show(this, "Failed Render", "Failed render due to:" + ex.Message);
                        });
                }
                finally
                {
                    Manager.ClearLastTask();
                    CurrentTask = null;
                    Dispatcher.UIThread.InvokeAsync(() => RaisePropertyChanged(IsRenderingProperty, true, false));
                }
            });
        }
        public async void RenderAnimation()
        {
            if (CurrentTask != null)
                return;

            //Validate provided fileformat
            if(!AnimationFileFormat.Contains("#"))
            {
                await MessageWindow.Show(this, "Invalid file format", "File format should contain a '#' for frame number");
                return;
            }
            string validAnimationFileName = AnimationFileFormat.Replace("#", "");
            if(Path.GetInvalidFileNameChars().Any(x=>validAnimationFileName.Contains(x)))
            {
                await MessageWindow.Show(this, "Invalid file format", "File name for animation frames contains illegal characters");
                return;
            }
            string animationFileFormat = AnimationFileFormat;



            string outputDir = "Animation";

            //Request output directory and UI
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                outputDir = null;

                OpenFolderDialog dialog = new OpenFolderDialog()
                {
                    Title = "Select folder to save animation frames to"
                };

                if (!string.IsNullOrEmpty(_lastAnimationDirectory))
                    dialog.Directory = _lastAnimationDirectory;

                outputDir = await dialog.ShowAsync(this);

                this._imageProgress.IsVisible = true;
                this._imageProgress.IsIndeterminate = true;
            });

            if (string.IsNullOrEmpty(outputDir))
                return;
            else
                outputDir = Path.GetFullPath(outputDir);

            _lastAnimationDirectory = outputDir;

            if (Manager.Nodes.Any(x => x.Connected && !x.IsSynced))
            {
                if (await YesNoNeverWindow.Show(this, "Unsynced nodes", "You have nodes that are not yet synced, would you like to sync them to use for rendering?", "syncBeforeRendering"))
                    await Manager.Sync();
            }

            await Task.Run(async () =>
            {
                try
                {
                    Stopwatch watch = new Stopwatch();
                    watch.Start();


                    //Create Task
                    CurrentTask = Manager.GetRenderTask(GetSettingsFromUI(), null, async (task, frame)=>
                    {
                        string filePath = Path.Combine(outputDir, animationFileFormat.Replace("#", task.Frame.ToString()));

                        try
                        {
                            frame.Save(filePath);
                        }
                        catch(Exception ex)
                        {
                            await MessageWindow.ShowOnUIThread(this, "Frame Save Error", $"Animation frame {task.Frame} failed to save due to:" + ex.Message);
                            return;
                        }

                        //Apply image to canvas
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            try
                            {
                                _lastBitmap = new Bitmap(filePath);
                                _image.Source = _lastBitmap;
                            }
                            catch(Exception ex)
                            {
                                _ = MessageWindow.Show(this, "GUI Exception", "An error occured trying to load animation Bitmap in GUI.\n(Animation frame should still be saved)");
                            }
                            _lastRenderTime.Text = watch.Elapsed.ToString();
                        });
                    });

                    //Progress Updating
                    CurrentTask.OnProgress += async (task, progress) =>
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            this._imageProgress.IsIndeterminate = false;
                            this._imageProgress.Value = progress * 100;
                        });
                    };

                    await Dispatcher.UIThread.InvokeAsync(() => RaisePropertyChanged(IsRenderingProperty, false, true));

                    //Render
                    var success = await CurrentTask.RenderAnimation(FrameStart, FrameEnd);
                    if (success)
                        _ = MessageWindow.ShowOnUIThread(this, "Animation Rendered", $"Frames {FrameStart} to {FrameEnd} rendered.\nLocated at {outputDir}.");

                    watch.Stop();

                }
                catch (Exception ex)
                {
                    await MessageWindow.ShowOnUIThread(this, "Failed Render", "Failed render due to:" + ex.Message);
                }
                finally
                {
                    Manager.ClearLastTask();
                    CurrentTask = null;
                    await Dispatcher.UIThread.InvokeAsync(() => RaisePropertyChanged(IsRenderingProperty, true, false));
                }
            });
        }

        public async Task CancelRender()
        {
            await CurrentTask?.Cancel();
            CurrentTask = null;
        }

        private static Bitmap FromDrawingBitmap(System.Drawing.Bitmap bitmap)
        {
            //TODO: This needs to be better..
            using(MemoryStream str = new MemoryStream())
            {
                bitmap.Save(str, ImageFormat.Png);
                str.Position = 0;
                return new Bitmap(str);
            }
        }

        public async void SaveImage()
        {
            SaveFileDialog dialog = new SaveFileDialog()
            {
                Title = "Save current BlendFarm render"
            };
            dialog.InitialFileName = "render.png";

            string result = await dialog.ShowAsync(this);
            if (result != null && _lastBitmap != null)
                _lastBitmap.Save(result);
        }


        public void StartLiveRender()
        {
            if (!IsLiveChanging)
            {
                IsLiveChanging = true;
                Manager.OnFileChanged += RenderOnFileChange;
                Manager.AlwaysUpdateFile = true;
                RaisePropertyChanged(IsLiveChangingProperty, false, true);
            }
        }
        public void StopLiveRender()
        {
            Manager.AlwaysUpdateFile = false;
            Manager.OnFileChanged -= RenderOnFileChange;
            IsLiveChanging = false;
            RaisePropertyChanged(IsLiveChangingProperty, true, false);
        }

        private RenderManagerSettings GetSettingsFromUI()
        {
            return new RenderManagerSettings()
            {
                Frame = FrameStart,
                Strategy = (RenderStrategy)_selectStrategy.SelectedItem,
                Order = (TaskOrder)_selectOrder?.SelectedItem,
                OutputHeight = RenderHeight,
                OutputWidth = RenderWidth,
                ChunkHeight = ((decimal)ChunkSize / RenderHeight),
                ChunkWidth = ((decimal)ChunkSize / RenderWidth),
                Samples = Samples,
                FPS = (UseFPS) ? FPS : 0,
                Denoiser = (Denoiser == "Inherit") ? "" : Denoiser ?? "",
                BlenderUpdateBugWorkaround = UseWorkaround,
                UseAutoPerformance = UseAutomaticPerformance
            };
        }

        private void RenderOnFileChange(BlendFarmManager manager)
        {
            if (CurrentTask?.Progress <= 0)
                return;
            Task.Run(async () =>
            {
                if (IsRendering)
                {
                    await CancelRender();
                }
                if (!IsRendering)
                {
                    await SyncAll();
                    await Render(true, true);
                }
            });
        }


        public void Github()
        {
            OpenUrl("https://github.com/LogicReinc/LogicReinc.BlendFarm");
        }
        public void Patreon()
        {
            OpenUrl("https://www.patreon.com/LogicReinc");
        }
        public void Help()
        {
            OpenUrl("https://www.youtube.com/watch?v=EXdwD5t53wc");
        }
        private static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }


    }
}
