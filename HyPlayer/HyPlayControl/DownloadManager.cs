﻿#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using HyPlayer.Classes;
using NeteaseCloudMusicApi;
using TagLib;
using File = TagLib.File;
using Windows.Graphics.Imaging;
using Microsoft.Toolkit.Uwp.Helpers;

#endregion

namespace HyPlayer.HyPlayControl;

internal class DownloadObject
{
    private PlayItem dontuseme;
    public DownloadOperation downloadOperation;

    public string fullpath;
    public ulong HavedSize;
    public NCSong ncsong;

    public int progress;
    public bool completedFired = false;

    // 0 - 排队 1 - 下载中 2 - 下载完成  3 - 暂停
    public int Status;
    public ulong TotalSize;

    private static Dictionary<string, Guid> CodecIds = new Dictionary<string, Guid>()
    {
        { "image/pjpeg", BitmapDecoder.JpegDecoderId },
        { "image/x-png", BitmapDecoder.PngDecoderId },
        { "image/webp", BitmapDecoder.WebpDecoderId }
    };

    public DownloadObject(NCSong song)
    {
        ncsong = song;
    }

    public string filename;

    private void Wc_DownloadFileCompleted()
    {
        DownloadManager.WritingTasks.Add(Task.Run(async () =>
        {
            if (Common.Setting.downloadLyric)
                await DownloadLyric().ConfigureAwait(false);
            if (Common.Setting.writedownloadFileInfo)
                await WriteInfoToFile().ConfigureAwait(false);
            DownloadManager.WritingTasks.RemoveAll(t => t.IsCompleted);
        }));

        /*
        try
        {
            var downloadToastContent = new ToastContent
            {
                Visual = new ToastVisual
                {
                    BindingGeneric = new ToastBindingGeneric
                    {
                        Children =
                        {
                            new AdaptiveText
                            {
                                Text = "下载完成",
                                HintStyle = AdaptiveTextStyle.Header
                            },
                            new AdaptiveText
                            {
                                Text = filename
                            }
                        }
                    }
                },
                Launch = "",
                Scenario = ToastScenario.Reminder,
                Audio = new ToastAudio { Silent = true }
            };
            var toast = new ToastNotification(downloadToastContent.GetXml())
            {
                Tag = "HyPlayerDownloadDone",
                Data = new NotificationData()
            };
            toast.Data.SequenceNumber = 0;
            var notifier = ToastNotificationManager.CreateToastNotifier();
            notifier.Show(toast);
            
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
        */
        Common.AddToTeachingTipLists("下载完成", filename);
    }

    private Task WriteInfoToFile()
    {
        return Task.Run(async () =>
        {
            try
            {
                /*
                var streamAbscraction = new UwpStorageFileAbstraction(
                    (await downloadOperation.GetResultRandomAccessStreamReference().OpenReadAsync()).AsStreamForRead(),
                    await downloadOperation.ResultFile.OpenStreamForWriteAsync(), downloadOperation.ResultFile.Name);
                    */
                var streamAbscraction = new UwpStorageFileAbstraction(downloadOperation.ResultFile);
                var file = File.Create(streamAbscraction);
                if (Common.Setting.write163Info)
                    The163KeyHelper.TrySetMusicInfo(file.Tag, dontuseme);
                //写相关信息
                file.Tag.Album = ncsong.Album.name;
                file.Tag.Performers = ncsong.Artist.Select(t => t.name).ToArray();
                file.Tag.Title = ncsong.songname;
                file.Tag.Track = (uint)(ncsong.TrackId == -1 ? (ncsong.Order + 1) : ncsong.TrackId);
                file.Tag.Disc = uint.Parse(Regex.Match(ncsong.CDName, "[0-9]+").Value);
                //file.Save();

                Picture pic;
                if (!DownloadManager.AlbumPicturesCache.ContainsKey(ncsong.Album.id))
                {
                    var ras = RandomAccessStreamReference.CreateFromUri(new Uri(ncsong.Album.cover + "?param=" +
                        StaticSource
                            .PICSIZE_DOWNLOAD_ALBUMCOVER));
                    var httpStream = await ras.OpenReadAsync();
                    IRandomAccessStream outputStream;
                    if (httpStream.ContentType != "image/pjpeg")
                    {
                        var bitmapInput =
                            await (await BitmapDecoder.CreateAsync(CodecIds[httpStream.ContentType], httpStream))
                                .GetSoftwareBitmapAsync();
                        outputStream = new InMemoryRandomAccessStream();
                        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outputStream);
                        encoder.SetSoftwareBitmap(bitmapInput);
                        await encoder.FlushAsync();
                    }
                    else
                    {
                        outputStream = httpStream;
                    }

                    pic = new Picture(ByteVector.FromStream(outputStream.AsStreamForRead()));
                    DownloadManager.AlbumPicturesCache[ncsong.Album.id] = pic;
                }
                else
                {
                    pic = DownloadManager.AlbumPicturesCache[ncsong.Album.id];
                }

                file.Tag.Pictures = new IPicture[]
                {
                    pic
                };
                file.Tag.Pictures[0].MimeType = "image/jpeg";
                file.Tag.Pictures[0].Description = "cover.jpg";
                file.Save();
                streamAbscraction.Release();
            }
            catch (Exception ex)
            {
                Common.ErrorMessageList.Add("写入音乐信息时出现错误" + ex.Message);
                Common.AddToTeachingTipLists("写入音乐信息时出现错误", ex.Message);
            }
        });
    }

    private Task DownloadLyric()
    {
        //下载歌词
        return Task.Run(async () =>
        {
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Lyric,
                    new Dictionary<string, object> { { "id", ncsong.sid } });
                if (!(json.ContainsKey("nolyric") && json["nolyric"].ToString().ToLower() == "true") &&
                    !(json.ContainsKey("uncollected") && json["uncollected"].ToString().ToLower() == "true"))
                {
                    if (json["lrc"]["lyric"].ToString().Contains("[99:00.00]纯音乐，请欣赏"))
                    {
                        // 这个也是纯音乐
                        return;
                    }

                    var lrc = Utils.ConvertPureLyric(json["lrc"]["lyric"].ToString());
                    if (Common.Setting.downloadTranslation && json["tlyric"]?["lyric"] != null)
                        Utils.ConvertTranslation(json["tlyric"]["lyric"].ToString(), lrc);
                    var lrctxt = string.Join("\r\n", lrc.Select(t =>
                    {
                        if (t.HaveTranslation && !string.IsNullOrWhiteSpace(t.Translation))
                            return "[" + t.LyricTime.ToString(@"mm\:ss\.ff") + "]" + t.PureLyric + " 「" +
                                   t.Translation + "」";
                        return "[" + t.LyricTime.ToString(@"mm\:ss\.ff") + "]" + t.PureLyric;
                    }));
                    if (string.IsNullOrWhiteSpace(lrctxt)) return;
                    var sf = await (await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(fullpath)))
                        .CreateFileAsync(
                            Path.GetFileName(Path.ChangeExtension(fullpath, "lrc")),
                            CreationCollisionOption.ReplaceExisting);
                    if (Common.Setting.usingGBK)
                    {
                        await FileIO.WriteBytesAsync(sf,
                            Encoding.Convert(Encoding.UTF8, Encoding.GetEncoding("GBK"),
                                Encoding.UTF8.GetBytes(lrctxt)));
                    }
                    else
                    {
                        await FileIO.WriteTextAsync(sf, lrctxt);
                    }
                }
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        });
    }

    private void Wc_DownloadProgressChanged(DownloadOperation obj)
    {
        if (obj.Progress.TotalBytesToReceive == 0) return;
        TotalSize = obj.Progress.TotalBytesToReceive;
        HavedSize = obj.Progress.BytesReceived;
        progress = (int)(obj.Progress.BytesReceived * 100 / obj.Progress.TotalBytesToReceive);
        if (HavedSize == TotalSize)
        {
            if (Status == 2) return;
            Status = 2;
            completedFired = true;
            Wc_DownloadFileCompleted();
        }
    }

    public static void DownloadStartToast(string songname)
    {
        /*
        var downloadToastContent = new ToastContent
        {
            Visual = new ToastVisual
            {
                BindingGeneric = new ToastBindingGeneric
                {
                    Children =
                    {
                        new AdaptiveText
                        {
                            Text = "下载开始",
                            HintStyle = AdaptiveTextStyle.Header
                        },
                        new AdaptiveText
                        {
                            Text = songname
                        }
                    }
                }
            },
            Launch = "",
            Scenario = ToastScenario.Reminder,
            Audio = new ToastAudio { Silent = true }
        };
        var toast = new ToastNotification(downloadToastContent.GetXml())
        {
            Tag = "HyPlayerDownloadStart",
            Data = new NotificationData()
        };

        toast.Data.SequenceNumber = 0;
        var notifier = ToastNotificationManager.CreateToastNotifier();
        notifier.Show(toast);
        */
        Common.AddToTeachingTipLists("下载开始", "歌曲" + songname + "下载开始");
    }

    public async void StartDownload()
    {
        if (downloadOperation != null) return;
        Status = 1;
        try
        {
            filename = Common.Setting.downloadFileName
                .Replace("{$SINGER}", string.Join(';', ncsong.Artist.Select(t => t.name)).EscapeForPath())
                .Replace("{$SONGNAME}", ncsong.songname.EscapeForPath())
                .Replace("{$ALBUM}", ncsong.Album.name.EscapeForPath())
                .Replace("{$INDEX}",
                    (ncsong.TrackId == -1 ? (ncsong.Order + 1) : ncsong.TrackId).ToString().EscapeForPath())
                .Replace("{$CDNAME}", ncsong.CDName?.EscapeForPath());
            string folderName = Common.Setting.downloadDir;
            var nowFolder = await StorageFolder.GetFolderFromPathAsync(folderName);
            var ses = filename.Replace('\\', '/').Split('/');
            for (var index = 0; index < ses.Length - 1; index++)
            {
                var s = ses[index];
                folderName += "/" + s;
                nowFolder = await nowFolder.CreateFolderAsync(s, CreationCollisionOption.OpenIfExists);
            }

            if (await nowFolder.FileExistsAsync(Path.GetFileName(filename + ".mp3")) ||
                await nowFolder.FileExistsAsync(Path.GetFileName(filename + ".flac")))
            {
                switch (Common.Setting.downloadNameOccupySolution)
                {
                    case 0:
                        Common.AddToTeachingTipLists("文件已存在，自动跳过", ncsong.songname + "\n已自动将其从下载列表中移除");
                        DownloadManager.DownloadLists.Remove(DownloadManager.DownloadLists.FirstOrDefault());
                        return;
                    case 1:
                        await (await nowFolder.GetFileAsync(Path.GetFileName(filename))).DeleteAsync();
                        break;
                    case 2:
                        filename = Path.GetFileNameWithoutExtension(filename) + ncsong.sid;
                        break;
                }
            }

            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                new Dictionary<string, object> { { "id", ncsong.sid }, { "br", Common.Setting.downloadAudioRate } });

            if (json["data"][0]["code"].ToString() != "200")
            {
                Common.AddToTeachingTipLists("无法下载", "无法下载歌曲 " + ncsong.songname + "\n已自动将其从下载列表中移除");
                DownloadManager.DownloadLists.Remove(DownloadManager.DownloadLists.FirstOrDefault());
                return; //未获取到
            }

            filename += "." + json["data"][0]["type"].ToString().ToLowerInvariant();
            dontuseme = new PlayItem
            {
                Bitrate = json["data"][0]["br"].ToObject<int>(),
                Tag = "下载",
                Album = ncsong.Album,
                Artist = ncsong.Artist,
                SubExt = json["data"][0]["type"].ToString().ToLowerInvariant(),
                Id = ncsong.sid,
                Name = ncsong.songname,
                Type = HyPlayItemType.Netease,
                TrackId = ncsong.TrackId,
                CDName = ncsong.CDName,
                Url = json["data"][0]["url"].ToString(),
                LengthInMilliseconds = ncsong.LengthInMilliseconds,
                Size = json["data"][0]["size"].ToString()
                //md5 = json["data"][0]["md5"].ToString()
            };

            downloadOperation = DownloadManager.Downloader.CreateDownload(
                new Uri(json["data"][0]["url"].ToString()),
                await nowFolder.CreateFileAsync(Path.GetFileName(filename))
            );
            fullpath = downloadOperation.ResultFile.Path;
            //downloadOperation.IsRandomAccessRequired = true;
            var process = new Progress<DownloadOperation>(Wc_DownloadProgressChanged);
            _ = downloadOperation.StartAsync().AsTask(process);
            DownloadStartToast(filename);
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists("无法下载歌曲 " + ncsong.songname + "\n已自动将其从下载列表中移除", ex.Message);
            DownloadManager.DownloadLists.Remove(DownloadManager.DownloadLists.FirstOrDefault());
            Common.ErrorMessageList.Add("无法下载歌曲 " + ncsong.songname + "\n已自动将其从下载列表中移除" + ex.Message);
        }
    }
}

internal static class DownloadManager
{
    private static readonly Timer _timer = new Timer(1000);
    private static bool Timered;
    public static List<DownloadObject> DownloadLists = new();
    public static BackgroundDownloader Downloader = new();
    public static List<Task> WritingTasks = new();
    public static Dictionary<string, Picture> AlbumPicturesCache = new();

    public static bool CheckDownloadAbilityAndToast()
    {
        return true;
    }

    public static void AddDownload(NCSong song)
    {
        if (!CheckDownloadAbilityAndToast()) return;
        if (!Timered)
        {
            _timer.Elapsed += Timer_Elapsed;
            _timer.Start();
            Timered = true;
        }

        DownloadLists.Add(new DownloadObject(song));
    }

    private static void Timer_Elapsed(object sender, ElapsedEventArgs elapsedEventArgs)
    {
        if (DownloadLists.Count == 0) return;
        if (DownloadLists[0].Status == 1) return;
        if (DownloadLists[0].Status == 3)
            for (var i = 0; i < DownloadLists.Count; i++)
            {
                if (DownloadLists[i].Status == 2) DownloadLists.RemoveAt(i);
                if (DownloadLists[i].Status == 1) return;
                if (DownloadLists[i].Status == 0)
                {
                    DownloadLists[i].StartDownload();
                    return;
                }
            }

        if (DownloadLists[0].Status == 2)
        {
            DownloadLists.RemoveAt(0);
            if (DownloadLists.Count != 0) return;
            /*
            var downloadToastContent = new ToastContent
            {
                Visual = new ToastVisual
                {
                    BindingGeneric = new ToastBindingGeneric
                    {
                        Children =
                        {
                            new AdaptiveText
                            {
                                Text = "下载全部完成",
                                HintStyle = AdaptiveTextStyle.Header
                            }
                        }
                    }
                },
                Launch = "",
                Scenario = ToastScenario.Reminder,
                Audio = new ToastAudio { Silent = true }
            };
            var toast = new ToastNotification(downloadToastContent.GetXml())
            {
                Tag = "HyPlayerDownloadAllDone",
                Data = new NotificationData()
            };
            toast.Data.SequenceNumber = 0;
            var notifier = ToastNotificationManager.CreateToastNotifier();
            notifier.Show(toast);
            */
            Common.AddToTeachingTipLists("下载全部完成");
            AlbumPicturesCache.Clear();
            _timer.Stop();
            Timered = false;
            return;
        }

        if (DownloadLists[0].Status == 0) DownloadLists[0].StartDownload();
    }

    public static void AddDownload(List<NCSong> songs)
    {
        if (!CheckDownloadAbilityAndToast()) return;
        if (!Timered)
        {
            _timer.Elapsed += Timer_Elapsed;
            _timer.Start();
            Timered = true;
        }

        songs.ForEach(t => { DownloadLists.Add(new DownloadObject(t)); });
    }
}

public class UwpStorageFileAbstraction : File.IFileAbstraction
{
    private readonly IStorageFile file;


    public UwpStorageFileAbstraction(IStorageFile file)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file));

        this.file = file;
        _name = file.Name;
        _readStream = file.OpenStreamForReadAsync().GetAwaiter().GetResult();
        _writeStream = file.OpenStreamForWriteAsync().GetAwaiter().GetResult();
    }

    public UwpStorageFileAbstraction(Stream readStream, Stream writeStream, string name = "HyPlayer Music")
    {
        _readStream = readStream;
        _writeStream = writeStream;
        _name = name;
    }

    private readonly string _name;


    public string Name => _name;

    private readonly Stream _readStream;
    private readonly Stream _writeStream;

    public Stream ReadStream => _readStream;

    public Stream WriteStream => _writeStream;

    public void CloseStream(Stream stream)
    {
    }

    public void Release()
    {
        WriteStream.Close();
        WriteStream.Dispose();
        ReadStream.Close();
        ReadStream.Dispose();
    }
}