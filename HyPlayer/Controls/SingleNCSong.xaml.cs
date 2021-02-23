﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class SingleNCSong : UserControl
    {
        private NCSong ncsong;
        public SingleNCSong(NCSong song)
        {
            this.InitializeComponent();
            ncsong = song;
            ImageRect.ImageSource = new BitmapImage(new Uri(song.Album.cover+ "?param="+StaticSource.PICSIZE_SINGLENCSONG_COVER));
            TextBlockSongname.Text = song.songname;
            string artisttxt = "";
            song.Artist.ForEach(s => { artisttxt += s.name; });
            TextBlockArtist.Text = artisttxt;
        }

        public void AppendMe()
        {
            AudioPlayer.AppendNCSong(ncsong);
        }

        private void UIElement_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            AppendMe();
        }
    }
}
