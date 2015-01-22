using System;
using System.Collections.Generic;
using System.Net;
using System.Windows;


namespace PlayListManagement
{
    public enum MediaControlMessage
    {
        Play, Pause, Next
    }

    public interface PlayList
    {
        int Current { get; set; }

        //public PlayList()
        //{
        //    Songs = new List<Song>();
        //    Current = 0;

        //    var media = new MediaLibrary();
        //    var songs = media.Songs;

        //    foreach (var song in songs)
        //    {
        //        this.Songs.Add(song);
        //    }
        //}

        Uri Next();

        Uri Prev();
    }

    public class DummyPlayList : PlayList
    {
        private static DummyPlayList instance;
        private int current = 0;
        private String[] songs = { "ms-appx:///Audio/Two Steps From Hell - El Dorado (SkyWorld).mp3",
                                   "ms-appx:///Audio/Two Steps from Hell - Protectors of the Earth.mp3",
                                   "ms-appx:///Audio/Two_Steps_From_Hell_-_Heart_of_Courag_(mp3.pm).mp3"};

        public int Current { get; set; }
        public static DummyPlayList Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new DummyPlayList();
                }

                return instance;
            }
        }

        private DummyPlayList() { }

        public Uri Next()
        {
            current = (current + 1) % 3;
            return (new Uri(songs[current]));
        }

        public Uri Prev()
        {
            current = (current != 0) ? (current - 1) : 2;
            return (new Uri(songs[current]));
        }
    }
}
