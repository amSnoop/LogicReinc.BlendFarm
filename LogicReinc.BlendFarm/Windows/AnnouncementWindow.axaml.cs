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
    public partial class AnnouncementWindow : Window, INotifyPropertyChanged
    {
        public Announcement Announcement { get; set; }
        public Grid Segments { get; set; }
        public Grid Buttons { get; set; }
        public List<Announcement> Announcements { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public AnnouncementWindow()
        {
            Announcements = Announcement.GetAnnouncements(Constants.AnnouncementUrl).OrderByDescending(x => x.Date).ToList();
            Announcement = Announcements.FirstOrDefault();
            DataContext = this;

            InitializeComponent();
        }
        public AnnouncementWindow(List<Announcement> announcements)
        {
            Announcements = announcements?.OrderByDescending(x => x.Date).ToList();
            Announcement = announcements?.FirstOrDefault();
            DataContext = this;

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            int width = 600;//will be moved to style folder I think
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
        //Set up the main grid, filling each row with the appropriate type of content
        private void LoadMainGrid()
        {
            Segments.Children.Clear();//Leave the last two elenments to go in the row at the bottom of the page. They are assumed to always be a GitHub and Patreon button.
            for (int i = 0; i < Announcement.Segments.Count - 2; i++)
            {
                StorySegment seg = Announcement.Segments[i];
                Bitmap img = seg.BitmapFromText();
                Control item;


                if (seg.Type == "Image")
                {
                    item = new Image() { Source = img };
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
                        buttonContent = new Image() { Source = img };
                    else
                        buttonContent = new TextBlock(){ Text = seg.TextPart1 };
                    ((Button)item).Content = buttonContent;
                }


                else
                {
                    item = new TextBlock()
                    {
                        Text = Announcement.Segments[i].Text
                    };
                }
                Segments.RowDefinitions.Add(new RowDefinition(GridLength.Auto));//Allow each part to take up as much space as they need.
                item.Classes.Add(seg.Type);//This will be used for formatting them
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
            patreon.Background = new SolidColorBrush() { Color = Color.Parse("#f96854") };//This will be made into a brush at some point. This was just for testing.
            gitHub.SetValue(Grid.ColumnProperty, 0);
            patreon.SetValue(Grid.ColumnProperty, 1);
            Buttons.Children.Add(gitHub);
            Buttons.Children.Add(patreon);
        }

    }
}
