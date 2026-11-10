using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GamoireLaunchBox
{
    /// <summary>The embedded Gamoire icon as a menu image and a window icon, loaded once.</summary>
    public sealed class IconAssets
    {
        public const string ResourceName = "GamoireLaunchBox.icon.png";

        public IconAssets()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();

            Menu = System.Drawing.Image.FromStream(new MemoryStream(bytes));

            var frame = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze();
            Window = frame;
        }

        public System.Drawing.Image Menu { get; }

        public ImageSource Window { get; }
    }
}
