using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace LogicReinc.BlendFarm.Meta
{
    public class Announcement
    {
        public string Name { get; set; }
        public DateTime Date { get; set; }
        public List<StorySegment> Segments { get; set; }

        public string DateText => $"{Date.Year}-{Date.Month}-{Date.Day}";

        public static List<Announcement> GetAnnouncements(string url)
        {
            using HttpClient client = new HttpClient();
            List<Announcement> ann = JsonSerializer.Deserialize<List<Announcement>>(client.GetStringAsync(url).Result).ToList();
            foreach (var an in ann)
            {


                foreach (StorySegment s in an.Segments.ToList())
                {
                    if (s.Type == "" || s.Text == "")
                        an.Segments.Remove(s);
                }
            }
            return ann;
        }


    }

    public class StorySegment : INotifyPropertyChanged
    {
        private static Dictionary<string, Bitmap> _bitmapCache = new Dictionary<string, Bitmap>();


        public string Type { get; set; }
        public string Text { get; set; }
        public Bitmap Bitmap { get
            {
                return BitmapFromText();
            } }

        //Text Property
        public string TextPart1 => Text.Contains('|') ? Text.Split('|')[0] : Text;
        public string CmdURL => Text.Contains('|') ? Text.Split('|')[1] : Text;
        public event PropertyChangedEventHandler PropertyChanged;

        public Bitmap BitmapFromText()//I made this a method for some reason but I don't remember why now...
        {

            if (Type != "Image" && Type != "Button")
                return null;
            if (!Text.ToLower().StartsWith("http"))
                return null;
            if (_bitmapCache.ContainsKey(TextPart1))
                return _bitmapCache[TextPart1];

            try
            {
                using (HttpClient client = new())
                using (MemoryStream stream = new MemoryStream(client.GetByteArrayAsync(TextPart1).Result))
                {
                    Bitmap bitmap = new Bitmap(stream);
                    _bitmapCache.Add(TextPart1, bitmap);
                }
                if (_bitmapCache.ContainsKey(TextPart1))
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Bitmap)));
                return _bitmapCache[TextPart1];
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                if (!_bitmapCache.ContainsKey(TextPart1))
                    _bitmapCache.Add(TextPart1, null);
            }
            return null;

        }

        public void Execute()
        {
            if (CmdURL.ToLower().StartsWith("http"))
                Process.Start(new ProcessStartInfo(CmdURL)
                {
                    UseShellExecute = true
                });
        }

    }
}
