using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using NAPS2.Lang.Resources;
using NAPS2.Platform;
using NAPS2.Recovery;
using NAPS2.Scan.Images;
using NTwain.Data;
using ZXing;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace NAPS2.WinForms
{
    public partial class ThumbnailList : DragScrollListView
    // public partial class ThumbnailList : DragScrollListView
    {
        private static readonly FieldInfo imageSizeField;
        private static readonly MethodInfo performRecreateHandleMethod;
        private static int documentCount;
        private static List<Document> documents;

        static ThumbnailList()
        {
            documentCount = 1;
            documents = new List<Document>();
            // Try to enable larger thumbnails via a reflection hack
            if (PlatformCompat.Runtime.SetImageListSizeOnImageCollection)
            {
                imageSizeField = typeof(ImageList.ImageCollection).GetField("imageSize", BindingFlags.Instance | BindingFlags.NonPublic);
                performRecreateHandleMethod = typeof(ImageList.ImageCollection).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            else
            {
                imageSizeField = typeof(ImageList).GetField("imageSize", BindingFlags.Instance | BindingFlags.NonPublic);
                performRecreateHandleMethod = typeof(ImageList).GetMethod("PerformRecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            if (imageSizeField == null || performRecreateHandleMethod == null)
            {
                // No joy, just be happy enough with 256
                ThumbnailRenderer.MAX_SIZE = 256;

            }
        }

        // Private lock object: locking on the control itself can deadlock with Control.Invoke
        private readonly object syncRoot = new object();

        private Bitmap placeholder;

        // Native group settings already sent (see GroupRefresh). They are lost when the window handle is recreated.
        private readonly HashSet<ListViewGroup> groupsWithState = new HashSet<ListViewGroup>();
        private readonly Dictionary<ListViewGroup, string> groupFooters = new Dictionary<ListViewGroup, string>();

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            groupsWithState.Clear();
            groupFooters.Clear();
        }

        public ListViewGroupCollection GetGroups()
        {
            return Groups;
        }

        /// <summary>
        /// Gets the indices of the first and last items currently shown on screen (last is -1 when nothing is shown).
        /// Items are laid out in index order, so a binary search on their bounds is enough. Must run on the UI thread.
        /// </summary>
        public void GetVisibleRange(out int first, out int last)
        {
            first = 0;
            last = -1;
            int count = Items.Count;
            if (count == 0 || !IsHandleCreated || ClientSize.Height <= 0)
            {
                return;
            }
            int height = ClientSize.Height;

            // First item whose bottom is below the top of the view
            int lo = 0, hi = count - 1, found = count;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (Items[mid].Bounds.Bottom > 0)
                {
                    found = mid;
                    hi = mid - 1;
                }
                else
                {
                    lo = mid + 1;
                }
            }
            if (found == count)
            {
                return;
            }
            first = found;

            // Last item whose top is above the bottom of the view
            lo = first;
            hi = count - 1;
            found = first;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (Items[mid].Bounds.Top < height)
                {
                    found = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            last = found;
        }

        public ThumbnailList()
        {
            InitializeComponent();
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            LargeImageList = ilThumbnailList;
            AddGroup("Document " + documentCount.ToString());
            OwnerDraw = true;
            InsertionMark.Index = -1;
        }

        private const int WM_RBUTTONDOWN = 0x0204;

        // The native ListView selects the item under the cursor on right-button down.
        // The message is still passed on so the context menu opens, but the previous selection is restored afterwards.
        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg == WM_RBUTTONDOWN)
            {
                var previousSelection = SelectedIndices.Cast<int>().ToList();
                base.WndProc(ref m);
                if (!SelectedIndices.Cast<int>().SequenceEqual(previousSelection))
                {
                    SelectedIndices.Clear();
                    foreach (int index in previousSelection)
                    {
                        SelectedIndices.Add(index);
                    }
                }
                return;
            }
            base.WndProc(ref m);
        }

        public ThumbnailRenderer ThumbnailRenderer { get; set; }

        public Size ThumbnailSize
        {
            get => ilThumbnailList.ImageSize;
            set
            {
                if (imageSizeField != null && performRecreateHandleMethod != null)
                {
                    // A simple hack to let the listview have larger thumbnails than 256x256
                    if (PlatformCompat.Runtime.SetImageListSizeOnImageCollection)
                    {
                        imageSizeField.SetValue(ilThumbnailList.Images, value);
                        performRecreateHandleMethod.Invoke(ilThumbnailList.Images, new object[] { });
                    }
                    else
                    {
                        imageSizeField.SetValue(ilThumbnailList, value);
                        performRecreateHandleMethod.Invoke(ilThumbnailList, new object[] { "ImageSize" });
                    }
                }
                else
                {
                    ilThumbnailList.ImageSize = value;
                }
            }
        }

        private string ItemText => PlatformCompat.Runtime.UseSpaceInListViewItem ? " " : "";

        private List<ScannedImage> CurrentImages => Items.Cast<ListViewItem>().Select(x => (ScannedImage)x.Tag).ToList();



        public void AddedImages(List<ScannedImage> allImages, Color color, bool recover = false)
        {

            lock (syncRoot)
            {
                if (Groups.Count < 1)
                {
                    AddGroup("Document 1");
                }

                BeginUpdate();
                try
                {
                    // The real thumbnail is used whenever it exists, also while a project is loading (recover):
                    // a thumbnail rendered before its item was created used to be replaced by the hourglass here,
                    // and since the page was already rendered it was never drawn again.
                    int existing = Math.Min(Math.Min(ilThumbnailList.Images.Count, Items.Count), allImages.Count);
                    for (int i = 0; i < existing; i++)
                    {
                        if (Items[i].Tag != allImages[i])
                        {
                            SetImageAt(i, GetThumbnail(allImages[i]));
                            Items[i].Tag = allImages[i];
                        }
                    }

                    // New items are created first and added in one call: adding them one by one and setting
                    // their text made the ListView insert each item separately.
                    var newItems = new List<ListViewItem>();
                    for (int i = ilThumbnailList.Images.Count; i < allImages.Count; i++)
                    {
                        AddImage(GetThumbnail(allImages[i]));
                        var text = allImages[i].Separator
                            ? (i + 1).ToString() + " Separator"
                            : (i + 1).ToString();
                        newItems.Add(new ListViewItem(text, i) { Tag = allImages[i], ForeColor = color });
                    }
                    if (newItems.Count > 0)
                    {
                        Items.AddRange(newItems.ToArray());
                    }

                    // Puts every page in its document group (only the pages that are not in it yet)
                    GroupRefresh(allImages);
                }
                finally
                {
                    EndUpdate();
                }
            }
            Invalidate();
        }

        public void DeletedImages(List<ScannedImage> allImages)
        {
            lock (syncRoot)
            {
                BeginUpdate();
                try
                {
                    if (allImages.Count == 0)
                    {
                        ilThumbnailList.Images.Clear();
                        Items.Clear();
                    }
                    else
                    {
                        // One pass instead of a full scan of the list for every removed page
                        var remaining = new HashSet<ScannedImage>(allImages);
                        var removedImageIndices = new List<int>();
                        for (int i = Items.Count - 1; i >= 0; i--)
                        {
                            var item = Items[i];
                            if (!remaining.Contains((ScannedImage)item.Tag))
                            {
                                removedImageIndices.Add(item.ImageIndex);
                                Items.RemoveAt(i);
                            }
                        }
                        if (removedImageIndices.Count > 0)
                        {
                            // Icons removed from the highest index down, so the other indices stay valid meanwhile
                            removedImageIndices.Sort();
                            for (int k = removedImageIndices.Count - 1; k >= 0; k--)
                            {
                                int imageIndex = removedImageIndices[k];
                                if (imageIndex >= 0 && imageIndex < ilThumbnailList.Images.Count)
                                {
                                    ilThumbnailList.Images.RemoveAt(imageIndex);
                                }
                            }
                            // Each remaining item moves down by the number of icons removed before its own
                            foreach (ListViewItem item in Items)
                            {
                                int old = item.ImageIndex;
                                int shift = CountLessThan(removedImageIndices, old);
                                if (shift > 0)
                                {
                                    item.ImageIndex = old - shift;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    EndUpdate();
                }
                GroupRefresh(allImages);
            }
            Invalidate();
        }
        
        // Number of values lower than value in a sorted list
        private static int CountLessThan(List<int> sorted, int value)
        {
            int lo = 0, hi = sorted.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (sorted[mid] < value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }
            return lo;
        }

        // Each change of an item is a message to the native ListView (and a redraw): only send real changes
        private static void SetItemText(ListViewItem item, string text, Color color)
        {
            if (item.Text != text)
            {
                item.Text = text;
            }
            if (item.ForeColor != color)
            {
                item.ForeColor = color;
            }
        }

        public void UpdatedImages(List<ScannedImage> images, List<int> selection, Color color)
        {
            lock (syncRoot)
            {
                BeginUpdate();
                try
                {
                    int min = selection == null || !selection.Any() ? 0 : selection.Min();
                    int max = selection == null || !selection.Any() ? images.Count : selection.Max() + 1;
                    max = Math.Min(max, Math.Min(images.Count, Items.Count));

                    for (int i = Math.Max(0, min); i < max; i++)
                    {
                        int imageIndex = Items[i].ImageIndex;
                        SetImageAt(imageIndex, GetThumbnail(images[i]));
                        Items[i].Tag = images[i];

                        var text = images[i].RecoveryIndexImage.isSeparator
                            ? (i + 1).ToString() + "/" + Items.Count.ToString() + " Separator"
                            : (i + 1).ToString() + "/" + Items.Count.ToString() + " ";
                        SetItemText(Items[i], text, color);
                    }
                }
                finally
                {
                    EndUpdate();
                }
            }
            Invalidate();
        }

        public void UpdateDescriptions(List<ScannedImage> allImages, Color color)
        {
            lock (syncRoot)
            {
                BeginUpdate();
                try
                {
                    int count = Math.Min(allImages.Count, Items.Count);
                    for (int i = 0; i < count; i++)
                    {
                        int imageIndex = Items[i].ImageIndex;
                        SetImageAt(imageIndex, GetThumbnail(allImages[i]));
                        Items[i].Tag = allImages[i];

                        var text = allImages[i].Separator
                            ? (i + 1).ToString() + "/" + Items.Count.ToString() + " Separator"
                            : (i + 1).ToString() + "/" + Items.Count.ToString() + " ";
                        SetItemText(Items[i], text, color);
                    }
                }
                finally
                {
                    EndUpdate();
                }
            }

        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            e.DrawDefault = true;
            //Using microsoft example to start my own owner draw list. Trying to create my own insertion mark with the groups enabled.
            //ListViewInsertionMark test;

            if (InsertionMark.Index > -1)
            {


                var pos = InsertionMark.Index;


                var mpos = this.PointToClient(MousePosition);
                Rectangle rec = Items[pos].Bounds;

                if (pos == 0)
                    pos = 1;

                Rectangle rec2 = Items[pos - 1].Bounds;


                rec.X = rec.Left - 2;

                if (Math.Abs(mpos.X - rec.Left) > Math.Abs(mpos.X - rec.Right))
                {
                    rec.X = rec.Right - 2;
                    var com1 = Math.Abs(mpos.X - rec.Right);
                    var com2 = Math.Abs(mpos.X - rec2.Right);

                    if (com1 > com2)
                    {
                        rec.X = rec2.Right - 2;
                        rec.Y = rec2.Y;
                    }

                }

                if (InsertionMark.AppearsAfterItem)
                {
                    rec.X = rec.Left - 2;
                }


                rec.Width = 4;
                using (Brush br = new SolidBrush(ForeColor))
                {
                    e.Graphics.FillRectangle(br, rec);
                }

                //For debug uses
                //e.Graphics.DrawRectangle(pen,Items[InsertionMark.Index].Bounds);
            }


        }

        public void AddGroup(string text)
        {
            this.Groups.Add(new ListViewGroup(text, HorizontalAlignment.Left));
        }

        public void DestroyGroups(List<ScannedImage> images)
        {
            for (int i = 0; i < images.Count; i++)
            {
                // Group define from separator
                if (images[i].Separator == true)
                {
                    images[i].Separator = false;
                    images[i].RecoveryIndexImage.isSeparator = false;
                    images[i].Save();
                }
            }
        }

        //This will rebuild the groups and the document list for the export later.
        public List<Document> GroupRefresh(List<ScannedImage> images)
        {
            lock (syncRoot)
            {
                // 1. Work out the documents (and so the groups) from the separators
                documents.Clear();
                var headers = new List<string>();
                var groupOfPage = new int[images.Count];

                documentCount = 1;
                headers.Add(MiscResources.Document + documentCount.ToString());
                Document document = new Document { };
                document.firstpage = 0;
                document.description = MiscResources.Document + " 1";
                document.lastpage = images.Count;

                for (int i = 0; i < images.Count; i++)
                {
                    // Group define from separator
                    if (images[i].Separator == true)
                    {
                        if (i > 0)
                            document.lastpage = i;

                        documents.Add(document);

                        document = new Document { };
                        document.firstpage = i;
                        document.description = MiscResources.Document + i.ToString();

                        string name = images[i].RecoveryIndexImage.SeparatorName;
                        document.name = Path.GetFileNameWithoutExtension(name);

                        documentCount = headers.Count + 1;
                        headers.Add(MiscResources.Document + documentCount.ToString() + ": " + document.name);
                    }
                    groupOfPage[i] = headers.Count - 1;
                }
                document.lastpage = images.Count;
                documents.Add(document);

                // 2. Update the groups in place. Before, every group was deleted and every page put back in one,
                // which made the ListView redo all its layout each time (each batch while loading, each move,
                // each delete). Existing groups are kept (which also keeps a collapsed group collapsed) and only
                // the pages that are not in their group are moved.
                BeginUpdate();
                try
                {
                    while (Groups.Count > headers.Count)
                    {
                        var removed = Groups[Groups.Count - 1];
                        Groups.RemoveAt(Groups.Count - 1);
                        groupsWithState.Remove(removed);
                        groupFooters.Remove(removed);
                    }
                    for (int g = 0; g < headers.Count; g++)
                    {
                        if (g < Groups.Count)
                        {
                            if (Groups[g].Header != headers[g])
                            {
                                Groups[g].Header = headers[g];
                            }
                        }
                        else
                        {
                            AddGroup(headers[g]);
                        }
                    }

                    // Items are created by AddedImages; pages without an item yet are placed when it is created
                    int count = Math.Min(images.Count, Items.Count);
                    for (int i = 0; i < count; i++)
                    {
                        var group = Groups[groupOfPage[i]];
                        var item = Items[i];
                        if (item.Group != group)
                        {
                            group.Items.Add(item);
                        }
                    }

                    // 3. Native group settings, only where something changed: each message is sent to the ListView
                    foreach (ListViewGroup group in Groups)
                    {
                        if (groupsWithState.Add(group))
                        {
                            SetGroupState(group, ListViewGroupState.Collapsible);
                        }
                        var footer = group.Items.Count.ToString() + MiscResources.PagesDoc;
                        if (!groupFooters.TryGetValue(group, out var current) || current != footer)
                        {
                            SetGroupFooter(group, footer);
                            groupFooters[group] = footer;
                        }
                    }
                }
                finally
                {
                    EndUpdate();
                }
                return documents;
            }


        }

        public void ReplaceThumbnail(int index, ScannedImage img)
        {
            ReplaceThumbnails(new[] { (index, img) });
        }

        /// <summary>
        /// Replaces the icons of several pages at once. Only the icons are redrawn (no BeginUpdate/EndUpdate,
        /// which redrew the whole list for every thumbnail).
        /// A page whose item does not exist yet is skipped: AddedImages uses its thumbnail when it creates the item.
        /// </summary>
        public void ReplaceThumbnails(IEnumerable<(int Index, ScannedImage Image)> updates)
        {
            lock (syncRoot)
            {
                foreach (var (index, img) in updates)
                {
                    if (index < 0 || index >= Items.Count)
                    {
                        continue;
                    }
                    var item = Items[index];
                    if (item.Tag != img || item.ImageIndex < 0 || item.ImageIndex >= ilThumbnailList.Images.Count)
                    {
                        continue;
                    }
                    var thumb = GetThumbnail(img);
                    if (thumb.Size == ThumbnailSize)
                    {
                        SetImageAt(item.ImageIndex, thumb);
                        Invalidate(item.Bounds);
                    }
                    else
                    {
                        ReleaseThumbnail(thumb);
                    }
                }
            }
        }

        public void RegenerateThumbnailList(List<ScannedImage> images, Color color, bool onlyText = false)
        {
            
            lock (syncRoot) {

                BeginUpdate();
                try
                {
                    if (!onlyText)
                    {
                        if (ilThumbnailList.Images.Count > 0)
                        {
                            ilThumbnailList.Images.Clear();
                        }
                        // Added one by one so each copy can be released once the ImageList has its pixels (see AddImage)
                        foreach (var image in images)
                        {
                            AddImage(GetThumbnail(image));
                        }
                    }

                    int count = Items.Count;
                    for (int i = 0; i < count && i < images.Count; i++)
                    {
                        var item = Items[i];
                        var text = images[i].Separator == true
                            ? (i + 1).ToString() + "/" + count.ToString() + MiscResources.Separator
                            : (i + 1).ToString() + "/" + count.ToString();
                        SetItemText(item, text, color);
                        if (item.ImageIndex != i)
                        {
                            item.ImageIndex = i;
                        }
                    }

                    GroupRefresh(images);
                }
                finally
                {
                    EndUpdate();
                }
            }

        }

        // Only used for bitmaps that are NOT handed to the ImageList (e.g. when no thumbnail is set).
        // The shared placeholder is cached for reuse and must not be disposed here.
        private void ReleaseThumbnail(Bitmap thumb)
        {
            if (thumb != null && !ReferenceEquals(thumb, placeholder))
            {
                thumb.Dispose();
            }
        }

        // Once its native handle exists, the ImageList copies the pixels and keeps no reference to the bitmap
        // (checked: the icons stay correct after the bitmap is disposed). Before the handle exists it keeps the
        // bitmap to build the handle later, so it must not be disposed then (that made broken thumbnails).
        // These copies were never released before: 64 KB per icon at 128 px, 256 KB at 256 px.
        private void ReleaseAfterImageList(Bitmap thumb)
        {
            if (ilThumbnailList.HandleCreated)
            {
                ReleaseThumbnail(thumb);
            }
        }

        private void SetImageAt(int index, Bitmap thumb)
        {
            // Setting an icon creates the handle if needed, so the copy can always be released afterwards
            ilThumbnailList.Images[index] = thumb;
            ReleaseAfterImageList(thumb);
        }

        private void AddImage(Bitmap thumb)
        {
            ilThumbnailList.Images.Add(thumb);
            ReleaseAfterImageList(thumb);
        }

        private Bitmap GetThumbnail(ScannedImage img)
        {
            lock (syncRoot)
            {
                var thumb = img.GetThumbnail();
                if (thumb == null)
                {
                    return RenderPlaceholder();
                }
                if (img.IsThumbnailDirty)
                {
                    thumb = DrawHourglass(thumb);
                }
                return thumb;
            }
        }

        private Bitmap RenderPlaceholder()
        {
            lock (syncRoot)
            {
                if (placeholder?.Size == ThumbnailSize)
                {
                    return placeholder;
                }
                placeholder?.Dispose();
                placeholder = new Bitmap(ThumbnailSize.Width, ThumbnailSize.Height);
                placeholder = DrawHourglass(placeholder);
                return placeholder;
            }
        }

        private Bitmap DrawHourglass(Image image)
        {
            var bitmap = new Bitmap(ThumbnailSize.Width, ThumbnailSize.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                var attrs = new ImageAttributes();
                attrs.SetColorMatrix(new ColorMatrix
                {
                    Matrix33 = 0.3f
                });
                g.DrawImage(image,
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    0,
                    0,
                    image.Width,
                    image.Height,
                    GraphicsUnit.Pixel,
                    attrs);
                attrs.Dispose();
                g.DrawImage(Icons.hourglass_grey, new Rectangle((bitmap.Width - 32) / 2, (bitmap.Height - 32) / 2, 32, 32));
            }
            image.Dispose();
            return bitmap;
        }
    }
}
