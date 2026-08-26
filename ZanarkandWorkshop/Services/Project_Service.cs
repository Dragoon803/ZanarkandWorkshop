using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Utils;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FFXProjectEditor.Services
{
    public partial class Project_Service : SingletonBase<Project_Service>
    {
        /******************************************
         * STATE
         ******************************************/
        // master folder
        [ObservableProperty][NotifyPropertyChangedFor(nameof(IsProjectLoaded))] public string? projectPath; // This should be private set but can't do it with ObservableProperty
        [ObservableProperty][NotifyPropertyChangedFor(nameof(IsProjectRegistered))] public ProjectManifest? activeProject;
        public bool IsProjectLoaded => ProjectPath != null && ProjectPath != "" && Directory.Exists(ProjectPath);
        public bool IsProjectRegistered => ActiveProject is not null;

        /******************************************
         * Public functions
         ******************************************/

        public static bool IsPathValid(string projectPath)
        {
            if (File.Exists(projectPath))
            {
                Debug.WriteLine("This is a file");
                return false;
            }
            if (!Directory.Exists(projectPath))
            {
                Debug.WriteLine("Directory doesn't exist");
                return false;
            }
            string normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
            if (!string.Equals(Path.GetFileName(normalizedPath), "master", System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("Directory is not the master folder");
                return false;
            }
            return true;
        }

        public void LoadProject(string projectPath)
        {
            if (!Directory.Exists(projectPath))
                throw new System.Exception("[Project_Service] Provided folder doesn't exist");

            string normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
            if (!string.Equals(Path.GetFileName(normalizedPath), "master", System.StringComparison.OrdinalIgnoreCase))
                throw new System.Exception("[Project_Service] Provided folder is not master folder");

            // Complete every fallible filesystem/registry operation before
            // publishing the new active path. A failed switch therefore leaves
            // the previous project and its visible editor consistently active.
            ProjectManifest? preparedProject = ProjectRegistry_Service.FindByPath(normalizedPath);
            ProgramMetadata_Service.MigrateKnownFiles();
            if (preparedProject is not null)
                ProjectRegistry_Service.MarkOpened(preparedProject);

            ProjectPath = normalizedPath;
            ActiveProject = preparedProject;
        }

        public ProjectManifest RegisterActiveProject(string name)
        {
            CheckProject();
            if (ActiveProject is not null) return ActiveProject;
            ActiveProject = ProjectRegistry_Service.Register(name, ProjectPath!);
            return ActiveProject;
        }

        /******************************************
         * Files
         ******************************************/
        public string Path_Btl => Path.Combine(ProjectPath, "jppc", "battle", "btl");
        public string Path_Kernel => Path.Combine(ProjectPath, "jppc", "battle", "kernel");
        public string Path_KernelUs => Path.Combine(ProjectPath, "new_uspc", "battle", "kernel");
        public string Path_KernelArmsRate => Path.Combine(Path_Kernel, "arms_rate.bin");
        public string Path_KernelCommand => Path.Combine(Path_Kernel, "command.bin");
        public string Path_KernelCommandUs => Path.Combine(Path_KernelUs, "command.bin");
        public string Path_KernelItemUs => Path.Combine(Path_KernelUs, "item.bin");
        public string Path_KernelMonMagic1Us => Path.Combine(Path_KernelUs, "monmagic1.bin");
        public string Path_KernelMonMagic2Us => Path.Combine(Path_KernelUs, "monmagic2.bin");
        public string Path_KernelAutoAbilityUs => Path.Combine(Path_KernelUs, "a_ability.bin");
        public string Path_KernelCustomization => Path.Combine(Path_Kernel, "kaizou.bin");
        public string Path_KernelMixRecipes => Path.Combine(Path_Kernel, "prepare.bin");
        public string Path_Mon => Path.Combine(ProjectPath, "jppc", "battle", "mon");
        public string Path_SphereGrid => Path.Combine(ProjectPath, "jppc", "menu", "abmap");
        public string Path_ZanarkandWorkshopMetadata
        {
            get
            {
                if (ActiveProject is not null)
                    return Path.Combine(ProjectRegistry_Service.ProjectsRoot, ActiveProject.Name);
                string projectPath = ProjectPath ??
                    throw new System.InvalidOperationException("No project is loaded.");
                string normalizedPath = Path.GetFullPath(projectPath).ToUpperInvariant();
                string projectHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)))[..12]
                    .ToLowerInvariant();
                string projectName = Path.GetFileName(
                    Directory.GetParent(projectPath)?.FullName ?? projectPath);
                string safeProjectName = new(
                    projectName.Select(character =>
                        Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)
                    .ToArray());
                return Path.Combine(
                    ProgramMetadata_Service.RootPath,
                    "projects",
                    $"{safeProjectName}-{projectHash}");
            }
        }

        public string Path_PathHashedProjectMetadata
        {
            get
            {
                string projectPath = ProjectPath ??
                    throw new InvalidOperationException("No project is loaded.");
                string normalizedPath = Path.GetFullPath(projectPath).ToUpperInvariant();
                string projectHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)))[..12].ToLowerInvariant();
                string projectName = Path.GetFileName(Directory.GetParent(projectPath)?.FullName ?? projectPath);
                string safeProjectName = new(projectName.Select(character =>
                    Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray());
                return Path.Combine(ProgramMetadata_Service.RootPath, "projects", $"{safeProjectName}-{projectHash}");
            }
        }

        public string Path_LegacyProjectMetadata =>
            Path.Combine(ProjectPath, ".zanarkand-workshop", "metadata");

        public string GetPathKernelMonsterUs(int monsterId)
        {
            CheckProject();
            if (monsterId < 0 || monsterId > 365)
                throw new System.Exception("[Project_Service] Invalid kernel monster id");

            int split = monsterId <= 100 ? 1 : monsterId <= 180 ? 2 : 3;
            return Path.Combine(Path_KernelUs, $"monster{split}.bin");
        }

        public string GetPathMon(int monsterId)
        {
            CheckProject();

            if (monsterId < 0 || monsterId > 999)
                throw new System.Exception("[Project_Service] Invalid monster id");

            string folder = "_m" + monsterId.ToString("D3");
            string file = "m" + monsterId.ToString("D3") + ".bin";
            return Path.Combine(Path_Mon, folder, file);
        }

        private void CheckProject()
        {
            if (!IsProjectLoaded)
                throw new System.Exception("[Project_Service] Project is not loaded!");
        }
    }
}
