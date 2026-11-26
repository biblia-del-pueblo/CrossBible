using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrossBible.Library.Downloads
{
    public class BookTranslation: InstallableBook
    {
        public static string RootPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Bible-Translations");
        public string FolderName { get; set; }
        public override string ConfigPath => Path.Combine(RootPath, FolderName);
        public override bool IsEditable => true;
        public static IEnumerable<BookTranslation> Translations => Directory.EnumerateDirectories(RootPath, "*.*", SearchOption.TopDirectoryOnly)
            .Select(dir => new BookTranslation() { FolderName = Path.GetFileName(dir) });
        public void CreateFrom(InstallableBook source)
        {
            foreach (var file in Directory.EnumerateFiles(source.ConfigPath, "*.*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(ConfigPath, Path.GetRelativePath(source.ConfigPath, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(file, dest, true);
            }
        }

        public override string Origin => "Translation";
        public override async Task Install() { }
        public override async Task Uninstall() { }
    }
}
