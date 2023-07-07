// Reference: System.Drawing
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;
using UnityEngine.Networking;
using Color = System.Drawing.Color;
using Graphics = System.Drawing.Graphics;

namespace Oxide.Plugins
{
    [Info("Egg Artist", "jarkypaisen", "1.0.0")]
    [Description("Allows players with the appropriate permission to import images from the internet on Egg suit")]

    public class EggArtist : RustPlugin
    {
        private Dictionary<ulong, string> eggRequestUrlList = new Dictionary<ulong, string>();
        private Dictionary<ulong, uint> eggFileStorageIdList = new Dictionary<ulong, uint>();
        EggArtistConfig Settings { get; set; }

        public class EggArtistConfig
        {
            [JsonProperty(PropertyName = "Maximum filesize in MB")]
            public float MaxSize { get; set; } = 1;

            [JsonIgnore]
            public float MaxFileSizeInBytes
            {
                get
                {
                    return MaxSize * 1024 * 1024;
                }
            }
        }
        protected override void SaveConfig() => Config.WriteObject(Settings);
        protected override void LoadConfig()
        {
            base.LoadConfig();
            Settings = Config.ReadObject<EggArtistConfig>();
            SaveConfig();
        }
        protected override void LoadDefaultConfig()
        {
            Settings = new EggArtistConfig();
        }

        private IEnumerator DownloadEggImage(PaintedItemStorageEntity entity, Item item, BasePlayer player)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(GetEggRequest(player)))
            {
                yield return request.SendWebRequest();
                if (request.isNetworkError || request.isHttpError)
                {
                    SendMessage(player, "WebErrorOccurred", request.error);
                }
                else
                {
                    if (request.downloadedBytes > Settings.MaxFileSizeInBytes)
                    {
                        SendMessage(player, "WebErrorOccurred");
                        yield break;
                    }
                    byte[] imageBytes = request.downloadHandler.data;
                    byte[] resizedImageBytes = ResizeImage(imageBytes, 512, 512, 512, 512);
                    if (resizedImageBytes.Length > Settings.MaxFileSizeInBytes)
                    {
                        SendMessage(player, "ErrorOccurred");
                        yield break;
                    }

                    if (HasEggFileStorageId(player))
                    {
                        FileStorage.server.Remove(GetEggFileStorageId(player), FileStorage.Type.png, entity.net.ID);
                    }
                    uint textureId = FileStorage.server.Store(resizedImageBytes, FileStorage.Type.png, entity.net.ID);
                    entity._currentImageCrc = textureId;
                    SetEggFileStorageId(player, textureId);
                    entity.SendNetworkUpdate();
                    SendMessage(player, "EggImageLoaded");
                }
            }
        }

        #region Init
        private void Init()
        {
            permission.RegisterPermission("eggartist.url", this);
            AddCovalenceCommand("egg", "EggCommand");
        }

        private void Unload()
        {
            eggRequestUrlList = null;
            eggFileStorageIdList = null;
        }

        #endregion Init
        #region Hook
        private void OnItemPainted(PaintedItemStorageEntity entity, Item item, BasePlayer player, byte[] image)
        {
            if (entity == null)
                return;
            if (item == null)
                return;
            if (player == null)
                return;
            if (entity.PrefabName != "assets/prefabs/misc/easter/egg_suit/item.painted.storage.prefab")
                return;
            if (HasEggRequest(player))
            {
                ServerMgr.Instance.StartCoroutine(DownloadEggImage(entity, item, player));
                RemEggRequest(player);
            }
        }
        #endregion Hook

        #region Localization
        protected override void LoadDefaultMessages()
        {
            // Register all messages used by the plugin in the Lang API.
            lang.RegisterMessages(new Dictionary<string, string>
            {
                // Messages used throughout the plugin.
                ["WebErrorOccurred"] = "Failed to download the image! Error {0}.",
                ["FileTooLarge"] = "The file exceeds the maximum file size of {0}Mb.",
                ["ErrorOccurred"] = "An unknown error has occured, if this error keeps occuring please notify the server admin.",
                ["EggUpdate"] = "Please open EggSuits paint and update it!",
                ["EggImageLoaded"] = "The image was succesfully loaded to the egg suit!",
                ["SyntaxEggCommand"] = "Syntax error!\nSyntax: /egg <url>",
                ["NoPermission"] = "You don't have permission to use this command.",
            }, this);
        }
        #endregion Localization

        #region Commands
        [Command("egg"), Permission("eggartist.url")]
        private void EggCommand(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if (!HasPermission(player, "eggartist.url"))
            {
                SendMessage(player, "NoPermission");
                return;
            }

            if (args.Length < 1)
            {
                SendMessage(player, "SyntaxSilCommand");
                return;
            }

            SetEggRequest(player, args[0]);
            SendMessage(player, "EggUpdate");
        }
        #endregion Commands

        #region Methods
        private bool HasEggRequest(BasePlayer player)
        {
            return eggRequestUrlList.ContainsKey(player.userID);
        }

        private string GetEggRequest(BasePlayer player)
        {
            return eggRequestUrlList[player.userID];
        }

        private void SetEggRequest(BasePlayer player, string url)
        {
            if (!eggRequestUrlList.ContainsKey(player.userID))
            {
                eggRequestUrlList.Add(player.userID, url);
                return;
            }
            eggRequestUrlList[player.userID] = url;
        }
        private void RemEggRequest(BasePlayer player)
        {
            eggRequestUrlList.Remove(player.userID);
        }
        private bool HasEggFileStorageId(BasePlayer player)
        {
            return eggFileStorageIdList.ContainsKey(player.userID);
        }

        private uint GetEggFileStorageId(BasePlayer player)
        {
            return eggFileStorageIdList[player.userID];
        }

        private void SetEggFileStorageId(BasePlayer player, uint id)
        {
            if (!eggFileStorageIdList.ContainsKey(player.userID))
            {
                eggFileStorageIdList.Add(player.userID, id);
                return;
            }
            eggFileStorageIdList[player.userID] = id;
        }
        private void RemEggFileStorageId(BasePlayer player)
        {
            eggFileStorageIdList.Remove(player.userID);
        }
        private byte[] ResizeImage(byte[] bytes, int width, int height, int targetWidth, int targetHeight)
        {
            byte[] resizedImageBytes;
            using (MemoryStream originalBytesStream = new MemoryStream(), resizedBytesStream = new MemoryStream())
            {
                originalBytesStream.Write(bytes, 0, bytes.Length);
                Bitmap image = new Bitmap(originalBytesStream);
                Color pixel = Color.FromArgb(UnityEngine.Random.Range(0, 256), UnityEngine.Random.Range(0, 256), UnityEngine.Random.Range(0, 256), UnityEngine.Random.Range(0, 256));
                if (image.Width != targetWidth || image.Height != targetHeight)
                {
                    Bitmap resizedImage = new Bitmap(width, height);
                    using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(resizedImage))
                    {
                        graphics.DrawImage(image, new Rectangle(0, 0, targetWidth, targetHeight));
                    }
                    resizedImage.SetPixel(resizedImage.Width - 1, resizedImage.Height - 1, pixel);
                    resizedImage.Save(resizedBytesStream, ImageFormat.Png);
                    resizedImageBytes = resizedBytesStream.ToArray();
                    resizedImage.Dispose();
                }
                else
                {
                    image.SetPixel(image.Width - 1, image.Height - 1, pixel);
                    resizedImageBytes = bytes;
                }
                image.Dispose();
            }
            return resizedImageBytes;
        }

        private bool HasPermission(BasePlayer player, string perm)
        {
            return permission.UserHasPermission(player.UserIDString, perm);
        }

        private void SendMessage(BasePlayer player, string key, params object[] args)
        {
            if (player == null) return;
            player.ChatMessage(string.Format(GetTranslation(key, player), args));
        }

        private string GetTranslation(string key, BasePlayer player = null)
        {
            return lang.GetMessage(key, this, player?.UserIDString);
        }
        #endregion Methods
    }
}
