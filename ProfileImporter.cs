using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WorkbenchHost
{
    internal sealed class ImportedProfileSettings
    {
        internal string Executable;
        internal string DisplayName;
        internal string ActivationPhrase;
        internal string Arguments;
        internal string ProcessName;
        internal string WindowClass;
        internal bool AttachExisting;
        internal bool CloseWithHost;
        internal bool FocusProtection;
        internal bool EnableGrayscale;
        internal int DefaultOpacity;
    }

    internal static class ApplicationProfileImporter
    {
        internal static string Import(string root, ImportedProfileSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            string executable = Path.GetFullPath(settings.Executable);
            if (!File.Exists(executable)) throw new FileNotFoundException("Application executable not found.", executable);
            if (String.IsNullOrWhiteSpace(settings.DisplayName)) throw new InvalidDataException("Display name is required.");
            if (String.IsNullOrWhiteSpace(settings.ActivationPhrase)) throw new InvalidDataException("Activation phrase is required.");
            string processName = String.IsNullOrWhiteSpace(settings.ProcessName)
                ? Path.GetFileNameWithoutExtension(executable)
                : settings.ProcessName.Trim();
            if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) processName = Path.GetFileNameWithoutExtension(processName);

            string profilesDirectory = Path.Combine(root, "profiles");
            if (!Directory.Exists(profilesDirectory)) Directory.CreateDirectory(profilesDirectory);
            string id = UniqueId(profilesDirectory, Slug(settings.DisplayName));
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["schemaVersion"] = 1;
            data["id"] = id;
            data["displayName"] = settings.DisplayName;
            data["windowTitle"] = settings.DisplayName + " workspace - Code";
            data["executable"] = executable;
            data["workingDirectory"] = Path.GetDirectoryName(executable);
            data["arguments"] = settings.Arguments ?? String.Empty;
            data["processName"] = processName;
            if (!String.IsNullOrWhiteSpace(settings.WindowClass)) data["windowClass"] = settings.WindowClass.Trim();
            data["attachExisting"] = settings.AttachExisting;
            data["launchTimeoutSeconds"] = 45;
            data["closeWithHost"] = settings.CloseWithHost;
            data["killAfterMilliseconds"] = 1500;
            data["activationPhrase"] = settings.ActivationPhrase.Trim();
            data["workspaceDirectory"] = ".";
            data["defaultOpacity"] = Math.Max(0, Math.Min(100, settings.DefaultOpacity));
            data["focusProtection"] = settings.FocusProtection;
            data["enableGrayscale"] = settings.EnableGrayscale;

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string path = Path.Combine(profilesDirectory, id + ".json");
            File.WriteAllText(path, serializer.Serialize(data), new UTF8Encoding(false));
            return path;
        }

        private static string Slug(string value)
        {
            string source = String.IsNullOrWhiteSpace(value) ? "application" : value.Trim().ToLowerInvariant();
            StringBuilder result = new StringBuilder();
            bool separator = false;
            foreach (char character in source)
            {
                if (Char.IsLetterOrDigit(character))
                {
                    result.Append(character);
                    separator = false;
                }
                else if (!separator && result.Length > 0)
                {
                    result.Append('-');
                    separator = true;
                }
            }
            string id = result.ToString().Trim('-');
            return id.Length == 0 ? "application" : id;
        }

        private static string UniqueId(string directory, string baseId)
        {
            string id = baseId;
            int suffix = 2;
            while (File.Exists(Path.Combine(directory, id + ".json"))) id = baseId + "-" + suffix++;
            return id;
        }
    }
}
