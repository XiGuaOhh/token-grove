using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace TokenGrove {
  public sealed class PlanImage {
    public string id, name, extension;
    public int width, height;
  }

  internal sealed class PlanImages {
    internal const int MaxCount = 24;
    internal const int MaxBytes = 25 * 1024 * 1024;
    const long MaxPixels = 32000000;
    static readonly Size ThumbnailSize = new Size(240, 160);
    internal readonly string DirectoryPath;

    internal PlanImages(string planFile) {
      DirectoryPath = Path.Combine(Path.GetDirectoryName(planFile), "plan-images");
    }

    internal static bool IsImageFile(string path) {
      if (string.IsNullOrEmpty(path)) return false;
      string extension = Path.GetExtension(path).ToLowerInvariant();
      return extension == ".png" || extension == ".jpg" || extension == ".jpeg" ||
        extension == ".jfif" || extension == ".bmp" || extension == ".gif";
    }

    internal static bool IsValid(PlanImage image) {
      return image != null && Guid.TryParseExact(image.id, "N", out _) &&
        !string.IsNullOrWhiteSpace(image.name) && image.name.Length <= 255 &&
        image.width > 0 && image.height > 0 && (long)image.width * image.height <= MaxPixels &&
        (image.extension == ".png" || image.extension == ".jpg" ||
          image.extension == ".bmp" || image.extension == ".gif");
    }

    string Original(PlanImage image) {
      if (!IsValid(image)) throw new InvalidDataException("图片记录无效。");
      return Path.Combine(DirectoryPath, image.id + image.extension);
    }

    string Thumbnail(PlanImage image) {
      return Path.Combine(DirectoryPath, image.id + ".thumb.png");
    }

    internal PlanImage ImportFile(string path, CancellationToken cancellation) {
      using (var source = File.OpenRead(path))
        return Import(source, Path.GetFileName(path), cancellation);
    }

    internal PlanImage ImportBitmap(Image bitmap, CancellationToken cancellation) {
      CheckSize(bitmap);
      cancellation.ThrowIfCancellationRequested();
      using (var png = new MemoryStream()) {
        bitmap.Save(png, ImageFormat.Png);
        png.Position = 0;
        return Import(png, "截图 " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", cancellation);
      }
    }

    internal PlanImage Import(Stream source, string name, CancellationToken cancellation) {
      if (source.CanSeek && source.Length - source.Position > MaxBytes)
        throw new InvalidDataException("单张图片最大 25 MB。");
      cancellation.ThrowIfCancellationRequested();
      Directory.CreateDirectory(DirectoryPath);
      string id = Guid.NewGuid().ToString("N");
      string imageStage = Path.Combine(DirectoryPath, id + ".image.tmp");
      string thumbStage = Path.Combine(DirectoryPath, id + ".thumb.tmp");
      PlanImage attachment = null;
      bool originalMoved = false, complete = false;
      try {
        using (var file = new FileStream(imageStage, FileMode.CreateNew, FileAccess.ReadWrite)) {
          CopyLimited(source, file, cancellation);
          file.Position = 0;
          using (var decoded = Decode(file)) {
            string extension = ExtensionFor(decoded);
            OrientJpeg(decoded);
            attachment = new PlanImage {
              id = id, name = string.IsNullOrWhiteSpace(name) ? "图片" + extension : name,
              extension = extension, width = decoded.Width, height = decoded.Height
            };
            using (var thumbnail = Resize(decoded, ThumbnailSize))
              thumbnail.Save(thumbStage, ImageFormat.Png);
          }
        }
        cancellation.ThrowIfCancellationRequested();
        File.Move(imageStage, Original(attachment));
        originalMoved = true;
        File.Move(thumbStage, Thumbnail(attachment));
        complete = true;
        return attachment;
      } catch (ExternalException) {
        throw new InvalidDataException("无法读取这张图片。");
      } finally {
        TryDelete(imageStage);
        TryDelete(thumbStage);
        if (!complete && originalMoved) TryDelete(Original(attachment));
      }
    }

    internal static void CopyLimited(Stream source, Stream destination, CancellationToken cancellation) {
      var buffer = new byte[65536];
      int total = 0;
      int count;
      while ((count = source.Read(buffer, 0, buffer.Length)) > 0) {
        cancellation.ThrowIfCancellationRequested();
        total += count;
        if (total > MaxBytes) throw new InvalidDataException("单张图片最大 25 MB。");
        destination.Write(buffer, 0, count);
      }
    }

    static void CheckSize(Image image) {
      if (image.Width < 1 || image.Height < 1 || (long)image.Width * image.Height > MaxPixels)
        throw new InvalidDataException("图片像素过大，请缩小后再添加。");
    }

    static Image Decode(Stream file) {
      Image image;
      try {
        image = Image.FromStream(file, true, false);
      } catch (ArgumentException) {
        throw new InvalidDataException("无法读取这张图片。");
      } catch (OutOfMemoryException) {
        throw new InvalidDataException("无法读取这张图片。");
      }
      try {
        CheckSize(image);
        return image;
      } catch {
        image.Dispose();
        throw;
      }
    }

    static string ExtensionFor(Image image) {
      Guid format = image.RawFormat.Guid;
      if (format == ImageFormat.Png.Guid) return ".png";
      if (format == ImageFormat.Jpeg.Guid) return ".jpg";
      if (format == ImageFormat.Bmp.Guid) return ".bmp";
      if (format == ImageFormat.Gif.Guid) return ".gif";
      throw new InvalidDataException("请选择 PNG、JPG、BMP 或 GIF 图片。");
    }

    static void OrientJpeg(Image image) {
      const int orientationProperty = 0x0112;
      if (image.RawFormat.Guid != ImageFormat.Jpeg.Guid ||
          Array.IndexOf(image.PropertyIdList, orientationProperty) < 0) return;
      var value = image.GetPropertyItem(orientationProperty)?.Value;
      if (value == null || value.Length < 2) return;
      switch (BitConverter.ToUInt16(value, 0)) {
        case 2: image.RotateFlip(RotateFlipType.RotateNoneFlipX); break;
        case 3: image.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
        case 4: image.RotateFlip(RotateFlipType.Rotate180FlipX); break;
        case 5: image.RotateFlip(RotateFlipType.Rotate90FlipX); break;
        case 6: image.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
        case 7: image.RotateFlip(RotateFlipType.Rotate270FlipX); break;
        case 8: image.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
      }
    }

    static Bitmap Resize(Image image, Size limit) {
      double ratio = Math.Min(1, Math.Min((double)limit.Width / image.Width, (double)limit.Height / image.Height));
      int width = Math.Max(1, (int)Math.Round(image.Width * ratio));
      int height = Math.Max(1, (int)Math.Round(image.Height * ratio));
      var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
      try {
        using (var graphics = Graphics.FromImage(bitmap)) {
          graphics.CompositingMode = CompositingMode.SourceCopy;
          graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
          graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
          graphics.DrawImage(image, new Rectangle(0, 0, width, height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
        }
        return bitmap;
      } catch {
        bitmap.Dispose();
        throw;
      }
    }

    internal Bitmap LoadThumbnail(PlanImage image) {
      try {
        if (!File.Exists(Original(image))) return null;
        string path = Thumbnail(image);
        using (var file = File.OpenRead(path)) {
          if (file.Length > 1024 * 1024) return null;
          using (var thumbnail = Decode(file)) return Resize(thumbnail, ThumbnailSize);
        }
      } catch (InvalidDataException) { return null; }
      catch (IOException) { return null; }
      catch (UnauthorizedAccessException) { return null; }
      catch (ExternalException) { return null; }
    }

    internal Bitmap LoadPreview(PlanImage image, Size limit) {
      using (var file = File.OpenRead(Original(image))) {
        if (file.Length > MaxBytes) throw new InvalidDataException("单张图片最大 25 MB。");
        using (var decoded = Decode(file)) {
          OrientJpeg(decoded);
          return Resize(decoded, limit);
        }
      }
    }

    internal void RemoveFiles(PlanImage image) {
      if (!IsValid(image)) return;
      TryDelete(Original(image));
      TryDelete(Thumbnail(image));
    }

    static void TryDelete(string path) {
      try { File.Delete(path); }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }
}
