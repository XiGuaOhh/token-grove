using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;

namespace TokenGrove {
  static class PlanImageRegressionTests {
    internal static void Run(string root, Action<bool, string> check) {
      string folder = Path.Combine(root, "image-plans");
      Directory.CreateDirectory(folder);
      string path = Path.Combine(folder, "plans.json");
      var store = new WorkPlanStore(path);
      var plan = store.Create("claude", null, DateTime.UtcNow);
      plan.title = "参考截图";
      plan.body = "这段正文和截图应一起保留。";
      // Write the previous format, which has no image field.
      J.Write(path, new {
        version = 1,
        plans = new[] { new { plan.id, plan.title, plan.body, plan.provider, plan.createdUtc, plan.updatedUtc } }
      });
      store = WorkPlanStore.Open(path);
      plan = store.Items.Single();
      check(store.CanWrite && plan.images.Count == 0 && plan.title == "参考截图",
        "A text-only plan from the previous version opens with an empty image list");

      string source = Path.Combine(folder, "参考图.png");
      using (var bitmap = new Bitmap(420, 210))
      using (var graphics = Graphics.FromImage(bitmap)) {
        graphics.Clear(Color.RoyalBlue);
        bitmap.Save(source, ImageFormat.Png);
      }
      var attachment = store.Images.ImportFile(source, CancellationToken.None);
      store.SetImages(plan, new[] { attachment });
      File.Move(source, Path.Combine(folder, "原图已移动.png"));

      var reopened = WorkPlanStore.Open(path);
      var saved = reopened.Items.Single();
      using (var thumbnail = reopened.Images.LoadThumbnail(saved.images.Single()))
      using (var preview = reopened.Images.LoadPreview(saved.images.Single(), new Size(500, 500))) {
        check(reopened.CanWrite && saved.body == plan.body && saved.images.Single().name == "参考图.png" &&
            preview.Size == new Size(420, 210) && preview.GetPixel(12, 12).ToArgb() == Color.RoyalBlue.ToArgb() &&
            thumbnail != null && thumbnail.Width <= 240 && thumbnail.Height <= 160,
          "Imported images reopen with their original pixels and a small thumbnail after the source is moved");
      }
      bool refused = false;
      try { reopened.SetImages(saved, new[] { attachment, attachment }); }
      catch (InvalidDataException) { refused = true; }
      check(refused && saved.images.Count == 1 && WorkPlanStore.Open(path).Items.Single().images.Count == 1,
        "A failed image update restores the in-memory plan and keeps the saved attachment");

      string corrupt = Path.Combine(folder, "坏图.jpg");
      File.WriteAllText(corrupt, "Not an image");
      int existingFiles = Directory.GetFiles(reopened.Images.DirectoryPath).Length;
      refused = false;
      try { reopened.Images.ImportFile(corrupt, CancellationToken.None); }
      catch (InvalidDataException) { refused = true; }
      check(refused && Directory.GetFiles(reopened.Images.DirectoryPath).Length == existingFiles,
        "Invalid image contents leave the saved plan and its image files intact");

      reopened.SetImages(saved, new PlanImage[0]);
      var backup = WorkPlanStore.Open(path + ".bak");
      using (var oldPreview = backup.Images.LoadPreview(backup.Items.Single().images.Single(), new Size(100, 100))) {
        check(WorkPlanStore.Open(path).Items.Single().images.Count == 0 && oldPreview.Width == 100,
          "Removing an attachment keeps the immediately preceding plan backup viewable");
      }
      WorkPlanStore.Open(path).Save();
      check(Directory.GetFiles(reopened.Images.DirectoryPath).Length == 0,
        "The managed image files are removed once neither the plan nor its backup needs them");
    }
  }
}
