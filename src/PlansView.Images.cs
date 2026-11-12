using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TokenGrove {
  public sealed partial class PlansView {
    readonly CancellationTokenSource imageLifetime = new CancellationTokenSource();
    readonly ToolTip imageTips = new ToolTip { InitialDelay = 450, AutoPopDelay = 6000 };
    GroveButton addImage;
    PlanImageStrip imageStrip;
    bool importingImages;
    int ImageCount => selected?.images.Count ?? 0;
    float ImageSectionHeight => ImageCount == 0 ? 42 : 119;
    float ImageHeaderY => BodyBox.Bottom + 8;

    void InitializeImages() {
      addImage = Button("+ 添加图片", () => _ = PickImagesAsync());
      addImage.AccessibleName = "添加图片";
      imageStrip = new PlanImageStrip();
      Controls.Add(imageStrip);
      imageTips.SetToolTip(addImage, "选择图片文件，也可以拖入文件或按 Ctrl+V 粘贴截图");
      body.PasteImage = title.PasteImage = PastePlanImages;
      RegisterImageDrop(this);
      RegisterImageDrop(body);
      RegisterImageDrop(imageStrip);
    }

    void LayoutImages() {
      if (addImage == null) return;
      bool hasPlan = selected != null;
      float x = Split + 18;
      Place(addImage, W - 131, ImageHeaderY, 113, 28);
      Place(imageStrip, x, ImageHeaderY + 34, W - x - 18, 78);
      addImage.Visible = hasPlan;
      addImage.Enabled = hasPlan && C.Plans.CanWrite && !importingImages && ImageCount < PlanImages.MaxCount;
      imageStrip.Visible = hasPlan && ImageCount > 0;
    }

    void PaintImages(Graphics graphics, Palette palette) {
      string caption = importingImages ? "正在添加图片…" : ImageCount > 0 ? "图片  " + ImageCount + " 张" : "图片";
      Art.Text(graphics, caption, Split + 18, ImageHeaderY + 7, 10.5f, palette.Muted);
      if (!importingImages && ImageCount == 0)
        Art.Text(graphics, "拖入或 Ctrl+V 粘贴", Split + 57, ImageHeaderY + 7, 10.5f, palette.Subtle);
    }

    void RefreshImageStrip() {
      if (imageStrip == null || imageStrip.IsDisposed || Disposing) return;
      imageStrip.SuspendLayout();
      try {
        foreach (var child in imageStrip.Controls.Cast<Control>().ToArray()) {
          imageTips.SetToolTip(child, null);
          child.Dispose();
        }
        imageStrip.AutoScrollPosition = Point.Empty;
        if (!Visible || selected == null) return;
        var plan = selected;
        foreach (var image in plan.images) {
          var tile = new PlanImageTile(image, C.Plans.Images.LoadThumbnail(image), () => C.Colors,
            () => ShowImage(plan, image)) {
            Size = new Size((int)(88 * scale), (int)(58 * scale)),
            Margin = new Padding(0, 0, (int)(8 * scale), 0)
          };
          imageStrip.Controls.Add(tile);
          imageTips.SetToolTip(tile, image.name);
          RegisterImageDrop(tile);
        }
      } finally {
        imageStrip.ResumeLayout();
        LayoutImages();
      }
    }

    void RegisterImageDrop(Control target) {
      target.AllowDrop = true;
      target.DragEnter += (s, e) => {
        bool accepting = selected != null && C.Plans.CanWrite && !importingImages && ImageCount < PlanImages.MaxCount;
        e.Effect = accepting && e.Data.GetDataPresent(DataFormats.FileDrop, false) &&
          (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
      };
      target.DragDrop += (s, e) => {
        if (e.Data.GetData(DataFormats.FileDrop, false) is string[] paths) _ = AddImageFilesAsync(paths);
      };
    }

    async Task PickImagesAsync() {
      using (var picker = new OpenFileDialog {
        Title = "添加计划图片",
        Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.gif",
        Multiselect = true,
        RestoreDirectory = true,
        CheckFileExists = true
      }) {
        if (picker.ShowDialog(FindForm()) == DialogResult.OK) await AddImageFilesAsync(picker.FileNames);
      }
    }

    Task AddImageFilesAsync(string[] paths) {
      var images = paths.Where(PlanImages.IsImageFile).ToArray();
      if (images.Length == 0) {
        SetImageNotice("请选择 PNG、JPG、BMP 或 GIF 图片。");
        return Task.CompletedTask;
      }
      var storage = C.Plans.Images;
      return AddImagesAsync(images.Select(path =>
        new Func<CancellationToken, PlanImage>(token => storage.ImportFile(path, token))).ToArray());
    }

    bool PastePlanImages() {
      if (selected == null || C.Quitting) return false;
      try { return PasteImageData(Clipboard.GetDataObject()); }
      catch {
        SetImageNotice("剪贴板暂不可用，请稍后重试。");
        return true;
      }
    }

    internal bool PasteImageData(IDataObject data) {
      if (data == null || selected == null) return false;
      if (data.GetData(DataFormats.FileDrop, false) is string[] paths && paths.Any(PlanImages.IsImageFile)) {
        _ = AddImageFilesAsync(paths);
        return true;
      }
      if (data.GetDataPresent("PNG", false)) {
        var encoded = data.GetData("PNG", false);
        try {
          byte[] png = encoded as byte[];
          if (encoded is Stream stream) {
            using (var buffer = new MemoryStream()) {
              if (stream.CanSeek) stream.Position = 0;
              PlanImages.CopyLimited(stream, buffer, CancellationToken.None);
              png = buffer.ToArray();
            }
          }
          if (png != null) {
            var storage = C.Plans.Images;
            _ = AddImagesAsync(new Func<CancellationToken, PlanImage>[] { token => {
              using (var source = new MemoryStream(png, false))
                return storage.Import(source, "截图 " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", token);
            } });
            return true;
          }
        } catch (InvalidDataException error) {
          SetImageNotice(error.Message);
          return true;
        }
      }
      if (data.GetDataPresent(DataFormats.Bitmap, true) &&
          data.GetData(DataFormats.Bitmap, true) is Image bitmap) {
        _ = AddClipboardBitmapAsync(bitmap);
        return true;
      }
      return false;
    }

    async Task AddClipboardBitmapAsync(Image bitmap) {
      using (bitmap) {
        var storage = C.Plans.Images;
        await AddImagesAsync(new Func<CancellationToken, PlanImage>[] { token => storage.ImportBitmap(bitmap, token) });
      }
    }

    async Task AddImagesAsync(Func<CancellationToken, PlanImage>[] imports) {
      if (selected == null || !C.Plans.CanWrite || C.Quitting) return;
      if (importingImages) { SetImageNotice("当前图片正在保存。"); return; }
      if (imports.Length + ImageCount > PlanImages.MaxCount) {
        SetImageNotice("每个计划最多 " + PlanImages.MaxCount + " 张图片。");
        return;
      }
      if (!Flush()) return;

      var plan = selected;
      var store = C.Plans;
      var token = imageLifetime.Token;
      var staged = new List<PlanImage>();
      int failed = 0;
      string failure = null;
      bool saved = false;
      importingImages = true;
      SetImageNotice("");
      LayoutImages();
      try {
        await Task.Run(() => {
          foreach (var import in imports) {
            token.ThrowIfCancellationRequested();
            try { staged.Add(import(token)); }
            catch (InvalidDataException error) { failed++; failure = error.Message; }
            catch (IOException) { failed++; failure = "图片文件无法读取或保存。"; }
            catch (UnauthorizedAccessException) { failed++; failure = "图片文件无法访问。"; }
          }
        });
        if (IsDisposed || C.Quitting || !store.Items.Contains(plan)) return;
        if (staged.Count > 0) {
          store.SetImages(plan, plan.images.Concat(staged));
          saved = true;
          if (selected == plan) RefreshImageStrip();
          C.PlansChanged();
        }
        if (failed > 0 && selected == plan)
          SetImageNotice(failed + " 张图片未添加。" + failure);
      } catch (OperationCanceledException) {
      } catch {
        if (!IsDisposed && selected == plan) SetImageNotice("图片保存失败，请重试。");
      } finally {
        if (!saved) foreach (var image in staged) store.Images.RemoveFiles(image);
        importingImages = false;
        if (!IsDisposed) { LayoutImages(); Invalidate(); }
      }
    }

    void ShowImage(WorkPlan plan, PlanImage image) {
      var owner = FindForm();
      if (owner == null) return;
      using (var viewer = new PlanImagePreview(C.Plans.Images, image, owner, C.Colors, C.IsDark, C.Plans.CanWrite)) {
        if (viewer.ShowDialog(owner) != DialogResult.Yes) return;
      }
      if (!Flush()) return;
      try {
        C.Plans.SetImages(plan, plan.images.Where(item => item.id != image.id));
        if (selected == plan) RefreshImageStrip();
        C.PlansChanged();
      } catch {
        SetImageNotice("图片未移除，请重试。");
      }
    }

    void SetImageNotice(string text) { notice = text; Invalidate(); }

    void DisposeImages() {
      imageLifetime.Cancel();
      imageLifetime.Dispose();
      imageTips.Dispose();
    }
  }
}
