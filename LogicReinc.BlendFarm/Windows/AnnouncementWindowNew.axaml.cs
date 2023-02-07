using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LogicReinc.BlendFarm.Meta;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace LogicReinc.BlendFarm.Windows
{
    public partial class AnnouncementWindowNew : Window, INotifyPropertyChanged
    {
        public Announcement Announcement { get; set; }
        public Grid Segments { get; set; }
        public Grid Buttons { get; set; }
        public List<Announcement> Announcements { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public AnnouncementWindowNew()
        {
            Announcements = Announcement.GetAnnouncements(Constants.AnnouncementUrl).OrderByDescending(x => x.Date).ToList();
            Announcement = Announcements.FirstOrDefault();
            DataContext = this;

            InitializeComponent();
        }
        public AnnouncementWindowNew(List<Announcement> announcements)
        {
            Announcements = announcements?.OrderByDescending(x => x.Date).ToList();
            Announcement = announcements?.FirstOrDefault();
            DataContext = this;

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            this.AttachDevTools(new Avalonia.Input.KeyGesture(Avalonia.Input.Key.K));
            int width = 600;
            int height = 700;

            MinWidth = width;
            MinHeight = height;
            Width = width;
            Height = height;

            Segments = this.Find<Grid>("Segments");
            Buttons = this.Find<Grid>("Buttons");
            Title = "Announcements";

            this.Find<ComboBox>("announcementSelection").SelectionChanged += (a, b) =>
            {
                if (b.AddedItems.Count > 0)
                {
                    Announcement = b.AddedItems[0] as Announcement;
                    LoadMainGrid();
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Announcement)));
                }
            };

            LoadMainGrid();
            LoadBottomButtons();
            

        }

        private void LoadMainGrid()
        {
            Segments.Children.Clear();
            for (int i = 0; i < Announcement.Segments.Count - 2; i++)
            {
                StorySegment seg = Announcement.Segments[i];
                Bitmap img = seg.BitmapFromText();
                Control item;
                if (seg.Type == "Image")
                {
                    item = new Image();
                    ((Image)item).Source = img;
                }
                else if (seg.Type == "Button")
                {
                    item = new Button();
                    ((Button)item).Click += (a, b) =>
                    {
                        seg.Execute();
                    };
                    Control buttonContent;
                    if (seg.TextPart1.StartsWith("http"))
                    {
                        buttonContent = new Image();
                        ((Image)buttonContent).Source = img;
                    }
                    else
                    {
                        buttonContent = new TextBlock()
                        {
                            Text = seg.TextPart1
                        };
                    }
                    ((Button)item).Content = buttonContent;
                }
                else
                {
                    item = new TextBlock()
                    {
                        Text = Announcement.Segments[i].Text
                    };
                }
                Segments.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                item.Classes.Add(seg.Type);
                item.SetValue(Grid.RowProperty, i);
                Segments.Children.Add(item);
            }
        }

        public void LoadBottomButtons()
        {
            Button gitHub = new Button()
            {
                Content = Announcement.Segments[^2].TextPart1
            };
            Button patreon = new Button
            {
                Content = new Image()
                {
                    Source = Announcement.Segments[^1].BitmapFromText()
                },
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            };
            gitHub.Click += (a, b) =>
            {
                Announcement.Segments[^2].Execute();
            };
            patreon.Click += (a, b) =>
            {
                Announcement.Segments[^1].Execute();
            };
            patreon.Background = new SolidColorBrush() { Color = Color.Parse("#f96854") };
            gitHub.SetValue(Grid.ColumnProperty, 0);
            patreon.SetValue(Grid.ColumnProperty, 1);
            Buttons.Children.Add(gitHub);
            Buttons.Children.Add(patreon);
        }

    }
}
