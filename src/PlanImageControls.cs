using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TokenGrove {
  internal sealed class PlanImageStrip : FlowLayoutPanel {
    internal PlanImageStrip() {
      AutoScroll = true;
      WrapContents = false;
      FlowDirection = FlowDirection.LeftToRight;
      AccessibleName = "计划图片";
      DoubleBuffered = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e) {
      GlassSurface.Paint(this, e.Graphics, e.ClipRectangle, Parent?.BackColor ?? BackColor);
    }

    protected override void OnMouseWheel(MouseEventArgs e) {
      if (!HorizontalScroll.Visible) { base.OnMouseWheel(e); return; }
      int distance = (int)Math.Round(e.Delta * DeviceDpi / 96d * .75);
      AutoScrollPosition = new Point(Math.Max(0, -AutoScrollPosition.X - distance), 0);
      if (e is HandledMouseEventArgs handled) handled.Handled = true;
    }
  }

  internal sealed class PlanImageTile : Button {
    readonly Func<Palette> colors;
    readonly Bitmap thumbnail;
    bool hovered;

    internal PlanImageTile(PlanImage image, Bitmap bitmap, Func<Palette> palette, Action open) {
      colors = palette;
      thumbnail = bitmap;
      AccessibleName = "预览 " + image.name;
      AccessibleDescription = image.width + " × " + image.height;
      FlatStyle = FlatStyle.Flat;
      FlatAppearance.BorderSize = 0;
      Cursor = Cursors.Hand;
      SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
      Click += (s, e) => open();
      MouseEnter += (s, e) => { hovered = true; Invalidate(); };
      MouseLeave += (s, e) => { hovered = false; Invalidate(); };
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e) {
      var palette = colors();
      var graphics = e.Graphics;
      float scale = graphics.DpiX / 96f;
      GlassSurface.Paint(this, graphics, e.ClipRectangle, palette.Panel);
      graphics.SmoothingMode = SmoothingMode.AntiAlias;
      var bounds = new RectangleF(.5f, .5f, Width - 1f, Height - 1f);
      Art.Box(graphics, bounds, palette.Hover, palette.Line, 8 * scale);
      if (thumbnail != null) {
        float fit = Math.Min((Width - 6f * scale) / thumbnail.Width, (Height - 6f * scale) / thumbnail.Height);
        float width = thumbnail.Width * fit, height = thumbnail.Height * fit;
        var frame = new RectangleF((Width - width) / 2, (Height - height) / 2, width, height);
        var saved = graphics.Save();
        using (var clip = Art.Round(RectangleF.Inflate(bounds, -2 * scale, -2 * scale), 6 * scale))
          graphics.SetClip(clip, CombineMode.Intersect);
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.DrawImage(thumbnail, frame);
        graphics.Restore(saved);
      } else {
        Typography.Draw(graphics, "查看图片", Art.CachedFont(11 * scale), ClientRectangle, palette.Muted, StringAlignment.Center);
      }
      if (hovered || (Focused && ShowFocusCues)) {
        using (var edge = Art.Round(bounds, 8 * scale))
        using (var pen = new Pen(palette.Green, 1.5f * scale))
          graphics.DrawPath(pen, edge);
      }
    }

    protected override void Dispose(bool disposing) {
      if (disposing) thumbnail?.Dispose();
      base.Dispose(disposing);
    }
  }

  internal sealed class PlanImagePreview : Form {
    readonly PlanImages storage;
    readonly PlanImage image;
    readonly PictureBox picture;
    readonly GroveLabel caption, status;
    readonly GroveButton copy, detach;
    readonly Size previewLimit;

    internal PlanImagePreview(PlanImages storage, PlanImage image, Form owner, Palette palette, bool dark, bool canRemove) {
      this.storage = storage;
      this.image = image;
      Text = "图片预览";
      AccessibleName = Text;
      BackColor = palette.Bg;
      ForeColor = palette.Text;
      StartPosition = FormStartPosition.CenterParent;
      ShowInTaskbar = false;
      ShowIcon = false;
      MinimizeBox = false;
      KeyPreview = true;
      AutoScaleMode = AutoScaleMode.None;
      float dpi = owner.DeviceDpi / 96f;
      var area = Screen.FromControl(owner).WorkingArea;
      ClientSize = new Size(Math.Min((int)(960 * dpi), (int)(area.Width * .88)),
        Math.Min((int)(660 * dpi), (int)(area.Height * .82)));
      MinimumSize = new Size((int)(420 * dpi), (int)(320 * dpi));
      previewLimit = new Size(Math.Min(area.Width, 2560), Math.Min(area.Height, 1600));

      picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = palette.Hover,
        AccessibleName = image.name };
      status = new GroveLabel { Dock = DockStyle.Fill, Text = "正在打开图片…", TextAlign = ContentAlignment.MiddleCenter,
        BackColor = palette.Hover, ForeColor = palette.Muted, Font = Art.CachedFont(14 * dpi) };
      var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding((int)(16 * dpi)) };
      content.Controls.Add(picture);
      content.Controls.Add(status);
      status.BringToFront();
      var footer = new Panel { Dock = DockStyle.Bottom, Height = (int)(56 * dpi), BackColor = palette.Bg };
      caption = new GroveLabel { Text = image.name + "   " + image.width + " × " + image.height,
        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ForeColor = palette.Muted,
        BackColor = palette.Bg, Font = Art.CachedFont(11 * dpi) };
      copy = new GroveButton("复制图片", () => _ = CopyAsync()) { Colors = () => palette, Glyph = "copy", Enabled = false };
      detach = new GroveButton("移除图片", () => { DialogResult = DialogResult.Yes; Close(); }) {
        Colors = () => palette, Quiet = true, Visible = canRemove
      };
      footer.Controls.AddRange(new Control[] { caption, copy, detach });
      footer.Resize += (s, e) => {
        int inset = (int)(16 * dpi), buttonWidth = (int)(104 * dpi), height = (int)(33 * dpi);
        int right = footer.ClientSize.Width - inset, y = (footer.Height - height) / 2;
        copy.SetBounds(right - buttonWidth, y, buttonWidth, height);
        int gap = (int)(6 * dpi);
        detach.SetBounds(copy.Left - gap - buttonWidth, y, buttonWidth, height);
        int labelRight = canRemove ? detach.Left : copy.Left;
        caption.SetBounds(inset, y, Math.Max(1, labelRight - inset - gap), height);
      };
      Controls.Add(content);
      Controls.Add(footer);
      KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
      Shown += (s, e) => { WindowAppearance.Apply(this, palette, dark, false, false); _ = LoadImageAsync(); };
    }

    async Task LoadImageAsync() {
      try {
        var bitmap = await Task.Run(() => storage.LoadPreview(image, previewLimit));
        if (IsDisposed || !Visible) { bitmap.Dispose(); return; }
        picture.Image = bitmap;
        status.Visible = false;
        copy.Enabled = true;
      } catch {
        if (!IsDisposed) status.Text = "图片文件缺失或无法读取。";
      }
    }

    async Task CopyAsync() {
      copy.Enabled = false;
      try {
        using (var bitmap = await Task.Run(() => storage.LoadPreview(image, new Size(image.width, image.height)))) {
          if (IsDisposed || !Visible) return;
          Clipboard.SetImage(bitmap);
          copy.Text = "已复制";
        }
      } catch {
        if (!IsDisposed) copy.Text = "复制失败";
      } finally {
        if (!IsDisposed) copy.Enabled = true;
      }
    }

    protected override void Dispose(bool disposing) {
      if (disposing && picture?.Image != null) {
        var bitmap = picture.Image;
        picture.Image = null;
        bitmap.Dispose();
      }
      base.Dispose(disposing);
    }
  }
}
