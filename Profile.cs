using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace WorkbenchHost
{
    public sealed class WorkbenchFile
    {
        public string Group { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
    }

    public sealed class WorkbenchProfile
    {
        public int SchemaVersion { get; set; }
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string WindowTitle { get; set; }
        public string Executable { get; set; }
        public string WorkingDirectory { get; set; }
        public string Arguments { get; set; }
        public string ProcessName { get; set; }
        public string WindowClass { get; set; }
        public bool AttachExisting { get; set; }
        public int LaunchTimeoutSeconds { get; set; }
        public bool CloseWithHost { get; set; }
        public int KillAfterMilliseconds { get; set; }
        public string ActivationPhrase { get; set; }
        public string VirtualFileName { get; set; }
        public string VirtualSource { get; set; }
        public string WorkspaceDirectory { get; set; }
        public string TriggerFile { get; set; }
        public int DefaultOpacity { get; set; }
        public bool FocusProtection { get; set; }
        public bool EnableGrayscale { get; set; }
        public List<WorkbenchFile> Files { get; set; }

        public string RootDirectory { get; private set; }
        public string ProfilePath { get; private set; }

        public static WorkbenchProfile Load(string path, string root)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Profile not found.", path);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            WorkbenchProfile profile = serializer.Deserialize<WorkbenchProfile>(File.ReadAllText(path));
            if (profile == null) throw new InvalidDataException("Profile JSON is empty or invalid.");

            profile.RootDirectory = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            profile.ProfilePath = Path.GetFullPath(path);
            profile.ApplyDefaults();
            profile.Validate();
            return profile;
        }

        public string ResolvePath(string relativePath)
        {
            if (String.IsNullOrWhiteSpace(relativePath)) return RootDirectory;
            string full = Path.GetFullPath(Path.Combine(RootDirectory, relativePath));
            string prefix = RootDirectory + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                !full.Equals(RootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Profile path escapes the workspace: " + relativePath);
            }
            return full;
        }

        public string ResolveApplicationPath(string configuredPath)
        {
            if (String.IsNullOrWhiteSpace(configuredPath)) return RootDirectory;
            string expanded = Environment.ExpandEnvironmentVariables(configuredPath);
            if (Path.IsPathRooted(expanded)) return Path.GetFullPath(expanded);
            return ResolvePath(expanded);
        }

        private void ApplyDefaults()
        {
            if (SchemaVersion == 0) SchemaVersion = 1;
            if (String.IsNullOrWhiteSpace(DisplayName)) DisplayName = Id;
            if (String.IsNullOrWhiteSpace(WindowTitle)) WindowTitle = DisplayName + " - Code";
            if (String.IsNullOrWhiteSpace(WorkingDirectory)) WorkingDirectory = Path.GetDirectoryName(Executable);
            if (String.IsNullOrWhiteSpace(ProcessName) && !String.IsNullOrWhiteSpace(Executable))
                ProcessName = Path.GetFileNameWithoutExtension(Executable);
            if (Arguments == null) Arguments = String.Empty;
            if (LaunchTimeoutSeconds <= 0) LaunchTimeoutSeconds = 45;
            if (KillAfterMilliseconds <= 0) KillAfterMilliseconds = 1200;
            if (String.IsNullOrWhiteSpace(ActivationPhrase)) ActivationPhrase = "hello world";
            if (String.IsNullOrWhiteSpace(VirtualFileName)) VirtualFileName = "application.runtime";
            if (String.IsNullOrWhiteSpace(WorkspaceDirectory)) WorkspaceDirectory = RootDirectory;
            // Profiles created before config.yaml used db.go as the shared gateway.
            if (String.IsNullOrWhiteSpace(TriggerFile) || String.Equals(TriggerFile.Trim(), "db.go", StringComparison.OrdinalIgnoreCase))
                TriggerFile = "config.yaml";
            if (DefaultOpacity < 0 || DefaultOpacity > 100) DefaultOpacity = 100;
            if (Files == null) Files = new List<WorkbenchFile>();
        }

        public string ResolveWorkspaceDirectory()
        {
            string expanded = Environment.ExpandEnvironmentVariables(WorkspaceDirectory ?? RootDirectory);
            string full = Path.IsPathRooted(expanded) ? Path.GetFullPath(expanded) : ResolvePath(expanded);
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Workspace directory not found: " + full);
            return full;
        }

        public string ResolveTriggerFile()
        {
            string configured = Environment.ExpandEnvironmentVariables(TriggerFile ?? "config.yaml");
            if (Path.IsPathRooted(configured)) return Path.GetFullPath(configured);
            return ResolvePath(configured);
        }

        private void Validate()
        {
            if (SchemaVersion != 1) throw new InvalidDataException("Unsupported profile schemaVersion: " + SchemaVersion);
            if (String.IsNullOrWhiteSpace(Id)) throw new InvalidDataException("Profile id is required.");
            if (String.IsNullOrWhiteSpace(Executable)) throw new InvalidDataException("Profile executable is required.");

            string executablePath = ResolveApplicationPath(Executable);
            if (!File.Exists(executablePath)) throw new FileNotFoundException("Configured executable not found.", executablePath);
            if (!String.IsNullOrWhiteSpace(VirtualSource) && !File.Exists(ResolvePath(VirtualSource)))
                throw new FileNotFoundException("Virtual source file not found.", ResolvePath(VirtualSource));

            foreach (WorkbenchFile file in Files)
            {
                if (String.IsNullOrWhiteSpace(file.Name) || String.IsNullOrWhiteSpace(file.Path))
                    throw new InvalidDataException("Every files entry requires name and path.");
                string filePath = ResolvePath(file.Path);
                if (!File.Exists(filePath)) throw new FileNotFoundException("Configured source file not found.", filePath);
            }
        }
    }
}
