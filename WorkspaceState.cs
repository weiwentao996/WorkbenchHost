using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace WorkbenchHost
{
    public sealed class WorkspaceState
    {
        public string WorkspaceDirectory { get; set; }
        public List<string> OpenFiles { get; set; }
        public string ActiveFile { get; set; }
        public int WindowX { get; set; }
        public int WindowY { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public bool Maximized { get; set; }

        public static string StatePath
        {
            get
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkbenchHost");
                return Path.Combine(directory, "workspace-state.json");
            }
        }

        public static WorkspaceState Load()
        {
            try
            {
                if (!File.Exists(StatePath)) return NewState();
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                WorkspaceState state = serializer.Deserialize<WorkspaceState>(File.ReadAllText(StatePath));
                if (state == null) return NewState();
                if (state.OpenFiles == null) state.OpenFiles = new List<string>();
                return state;
            }
            catch
            {
                return NewState();
            }
        }

        public void Save()
        {
            string directory = Path.GetDirectoryName(StatePath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            File.WriteAllText(StatePath, serializer.Serialize(this));
        }

        private static WorkspaceState NewState()
        {
            return new WorkspaceState { OpenFiles = new List<string>() };
        }
    }
}
