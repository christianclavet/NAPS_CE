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

        private Bitmap placeholder;

        public ListViewGroupCollection GetGroups()
        {
            return Groups;
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

            lock (this)
            {
                if (Groups.Count < 1)
                {
                    AddGroup("Document 1");
                    GroupRefresh(allImages);
                }

                BeginUpdate();
                for (int i = 0; i < ilThumbnailList.Images.Count; i++)
                {
                    if (Items[i].Tag != allImages[i])
                    {
                        if (!recover)
                            ilThumbnailList.Images[i] = GetThumbnail(allImages[i]);
                        else
                            ilThumbnailList.Images[i] = RenderPlaceholder();

                        Items[i].Tag = allImages[i];
                    }
                }
                EndUpdate();

                for (int i = ilThumbnailList.Images.Count; i < allImages.Count; i++)
                {
                    if (!recover)
                        ilThumbnailList.Images.Add(GetThumbnail(allImages[i]));
                    else
                        ilThumbnailList.Images.Add(RenderPlaceholder());

                    Items.Add(ItemText, i).Tag = allImages[i];
                    var sep = allImages[i].Separator;
                    if (sep)
                    {
                        AddGroup("Document " + (Groups.Count + 1).ToString());
                        Items[i].Text = (i + 1).ToString() + " Separator";
                    } else
                    {
                        Items[i].Text = (i + 1).ToString();
                    }
                    Items[i].ForeColor = color;
                    Groups[Groups.Count - 1].Items.Add(Items[i]);
                    GroupRefresh(allImages);
                    
                }

            }
            Invalidate();
        }

        public void DeletedImages(List<ScannedImage> allImages)
        {
            lock (this)
            {
                BeginUpdate();
                if (allImages.Count == 0)
                {
                    ilThumbnailList.Images.Clear();
                    Items.Clear();
                }
                else
                {
                    var a = CurrentImages.Except(allImages).Count();
                    foreach (var oldImg in CurrentImages.Except(allImages))
                    {
                        var item = Items.Cast<ListViewItem>().First(x => x.Tag == oldImg);
                        foreach (ListViewItem item2 in Items)
                        {
                            if (item2.ImageIndex > item.ImageIndex)
                            {
                                item2.ImageIndex -= 1;
                            }
                        }

                        ilThumbnailList.Images.RemoveAt(item.ImageIndex);
                        Items.RemoveAt(item.Index);
                    }
                }
                EndUpdate();
                GroupRefresh(allImages);
            }
            Invalidate();
        }
        
        public void UpdatedImages(List<ScannedImage> images, List<int> selection, Color color)
        {
            lock (this)
            {
                BeginUpdate();
                int min = selection == null || !selection.Any() ? 0 : selection.Min();
                int max = selection == null || !selection.Any() ? images.Count : selection.Max() + 1;

                for (int i = min; i < max; i++)
                {
                    int imageIndex = Items[i].ImageIndex;
                    ilThumbnailList.Images[imageIndex] = GetThumbnail(images[i]);
                    Items[i].Tag = images[i];

                    if (images[i].RecoveryIndexImage.isSeparator)
                    {
                        Items[i].Text = (i + 1).ToString() + "/" + Items.Count.ToString() + " Separator";
                    }
                    else
                    {
                        Items[i].Text = (i + 1).ToString() + "/" + Items.Count.ToString() + " ";
                    }

                    Items[i].ForeColor = color;
                }
                EndUpdate();
            }
            Invalidate();
        }

        public void UpdateDescriptions(List<ScannedImage> allImages, Color color)
        {
            lock (this)
            {
                BeginUpdate();
                for (int i = 0; i < allImages.Count; i++)
                {
                    int imageIndex = Items[i].ImageIndex;
                    ilThumbnailList.Images[imageIndex] = GetThumbnail(allImages[i]);
                    Items[i].Tag = allImages[i];

                    if (allImages[i].Separator)
                    {
                        Items[i].Text = (i + 1).ToString() + "/" + Items.Count.ToString() + " Separator";
                    }
                    else
                    {
                        Items[i].Text = (i + 1).ToString() + "/" + Items.Count.ToString() + " ";
                    }

                    Items[i].ForeColor = color;
                }
                EndUpdate();
            }

        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            e.DrawDefault = true;
            //Using microsoft example to start my own owner draw list. Trying to create my own insertion mark with the groups enabled.
            //ListViewInsertionMark test;

            if (InsertionMark.Index > -1)
            {

                Pen pen = new Pen(ForeColor, 1);

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
                Brush br = new SolidBrush(ForeColor);
                e.Graphics.FillRectangle(br, rec);

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
            lock (this)
            {

                BeginUpdate();
          
                Groups.Clear();
                documents.Clear();

                documentCount = 1;
                AddGroup(MiscResources.Document + documentCount.ToString());
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

                        documentCount = Groups.Count + 1;
                        AddGroup(MiscResources.Document + documentCount.ToString() + ": " + document.name);
                    }
                    Groups[documentCount - 1].Items.Add(Items[i]);
                    SetGroupState(ListViewGroupState.Collapsible);
                    SetGroupFooter(Groups[documentCount - 1], (Groups[documentCount - 1].Items.Count).ToString() + MiscResources.PagesDoc);
                }
                document.lastpage = images.Count;
                documents.Add(document);
                EndUpdate();
                return documents;
            }


        }

        public void ReplaceThumbnail(int index, ScannedImage img)
        {
            lock (this)
            {
                BeginUpdate();
                var thumb = GetThumbnail(img);
                if (thumb.Size == ThumbnailSize)
                {
                    ilThumbnailList.Images[index] = thumb;
                    Invalidate(Items[index].Bounds);
                }
                EndUpdate();
            }
        }

        public void RegenerateThumbnailList(List<ScannedImage> images, Color color, bool onlyText = false)
        {
            
            lock (this) { 
                
                if (!onlyText)
                {
                    BeginUpdate();
                    if (ilThumbnailList.Images.Count > 0)
                    {
                        ilThumbnailList.Images.Clear();
                    }

                    var list = new List<Image>();
                    foreach (var image in images)
                    {
                        list.Add(GetThumbnail(image));
                    }

                    ilThumbnailList.Images.AddRange(list.ToArray());
                    EndUpdate();
                }
                
                foreach (ListViewItem item in Items)
                {
                    if (images[item.Index].Separator == true)
                    {
                        item.Text = (item.Index + 1).ToString() + "/" + Items.Count.ToString() + MiscResources.Separator;
                    } else
                    {
                        item.Text = (item.Index + 1).ToString() + "/" + Items.Count.ToString();
                    }
                    item.ImageIndex = item.Index;
                    item.ForeColor = color;
                

                }

                GroupRefresh(images);

            }

        }

        private Bitmap GetThumbnail(ScannedImage img)
        {
            lock (this)
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
            lock (this)
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
                g.DrawImage(Icons.hourglass_grey, new Rectangle((bitmap.Width - 32) / 2, (bitmap.Height - 32) / 2, 32, 32));
            }
            image.Dispose();
            return bitmap;
        }
    }
}
