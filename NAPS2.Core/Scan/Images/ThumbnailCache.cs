using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using NAPS2.Scan.Images.Transforms;
using NAPS2.Logging;

namespace NAPS2.Scan.Images
{
    /// <summary>
    /// Keeps rendered thumbnails on disk, in a "thumbs" folder next to the page files, so reopening a project
    /// reads small images instead of decoding every full page again.
    ///
    /// A cached file is only used when everything it depends on is identical: the page file (name, size and
    /// last write time), the list of transforms and the thumbnail size. Anything else simply misses the cache.
    /// The cache is optional: every error is ignored and the thumbnail is rendered normally.
    /// </summary>
    public static class ThumbnailCache
    {
        public const string FOLDER_NAME = "thumbs";

        private static readonly XmlSerializer TransformSerializer = new XmlSerializer(typeof(List<Transform>));

        /// <summary>
        /// Returns the cached thumbnail for this snapshot and size, or null when there is none.
        /// </summary>
        public static Bitmap TryLoad(ScannedImage.Snapshot snapshot, int size)
        {
            try
            {
                var path = GetCachePath(snapshot, size);
                if (path == null || !File.Exists(path))
                {
                    return null;
                }
                // Read the bytes first: the file is not kept locked and the bitmap does not depend on it
                using (var stream = new MemoryStream(File.ReadAllBytes(path)))
                using (var loaded = new Bitmap(stream))
                {
                    if (loaded.Width != size || loaded.Height != size)
                    {
                        return null;
                    }
                    return new Bitmap(loaded);
                }
            }
            catch (Exception e)
            {
                Log.ErrorException("Error reading a cached thumbnail", e);
                return null;
            }
        }

        /// <summary>
        /// Saves a rendered thumbnail for this snapshot and size. Older thumbnails of the same page are removed.
        /// </summary>
        public static void Store(ScannedImage.Snapshot snapshot, int size, Bitmap thumbnail)
        {
            try
            {
                var path = GetCachePath(snapshot, size);
                if (path == null)
                {
                    return;
                }
                var folder = Path.GetDirectoryName(path);
                Directory.CreateDirectory(folder);
                var fileName = Path.GetFileName(snapshot.Source.RecoveryFilePath);
                DeleteFiles(folder, fileName, keep: path);

                // Written to a temporary file then renamed, so a file is never read while half written
                var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                thumbnail.Save(tempPath, ImageFormat.Png);
                if (File.Exists(path))
                {
                    File.Delete(tempPath);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            catch (Exception e)
            {
                Log.ErrorException("Error writing a cached thumbnail", e);
            }
        }

        /// <summary>
        /// Removes every cached thumbnail of a page file (used when the page file is deleted).
        /// </summary>
        public static void Delete(string pageFilePath)
        {
            try
            {
                var folder = Path.Combine(Path.GetDirectoryName(pageFilePath) ?? "", FOLDER_NAME);
                if (Directory.Exists(folder))
                {
                    DeleteFiles(folder, Path.GetFileName(pageFilePath), keep: null);
                }
            }
            catch (Exception e)
            {
                Log.ErrorException("Error deleting cached thumbnails", e);
            }
        }

        private static void DeleteFiles(string folder, string pageFileName, string keep)
        {
            foreach (var file in Directory.EnumerateFiles(folder, pageFileName + ".*"))
            {
                if (keep != null && string.Equals(file, keep, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // In use (being read by another thread): it will be replaced or removed later
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        // <page folder>\thumbs\<page file name>.<key>.png
        private static string GetCachePath(ScannedImage.Snapshot snapshot, int size)
        {
            var pagePath = snapshot.Source.RecoveryFilePath;
            var info = new FileInfo(pagePath);
            if (!info.Exists || info.DirectoryName == null)
            {
                return null;
            }

            var keyText = new StringBuilder();
            keyText.Append(info.Length).Append('|')
                .Append(info.LastWriteTimeUtc.Ticks).Append('|')
                .Append(size).Append('|');
            if (snapshot.TransformList.Count > 0)
            {
                using (var writer = new StringWriter())
                {
                    TransformSerializer.Serialize(writer, snapshot.TransformList.ToList());
                    keyText.Append(writer);
                }
            }

            string key;
            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(keyText.ToString()));
                key = BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();
            }
            return Path.Combine(info.DirectoryName, FOLDER_NAME, info.Name + "." + key + ".png");
        }
    }
}
