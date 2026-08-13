using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace WorkbenchHost
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            try
            {
                IconPainter.InitializeCodicons(root);
                List<WorkbenchProfile> profiles = LoadProfiles(args, root);
                Application.Run(new MainForm(root, profiles));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Workbench Host", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 1;
            }
        }

        private static List<WorkbenchProfile> LoadProfiles(string[] args, string root)
        {
            List<WorkbenchProfile> profiles = new List<WorkbenchProfile>();
            if (args.Length > 0)
            {
                string requested = args[0];
                if (!Path.IsPathRooted(requested)) requested = Path.Combine(root, requested);
                profiles.Add(WorkbenchProfile.Load(requested, root));
                return profiles;
            }

            string profileDirectory = Path.Combine(root, "profiles");
            if (!Directory.Exists(profileDirectory)) Directory.CreateDirectory(profileDirectory);
            string[] paths = Directory.GetFiles(profileDirectory, "*.json");
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                try { profiles.Add(WorkbenchProfile.Load(path, root)); }
                catch { }
            }
            return profiles;
        }
    }
}
