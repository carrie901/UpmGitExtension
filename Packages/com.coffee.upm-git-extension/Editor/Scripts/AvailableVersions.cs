#if OPEN_SESAME // This line is added by Open Sesame Portable. DO NOT remov manually.
#if UNITY_2019_1_9 || UNITY_2019_1_10 || UNITY_2019_1_11 || UNITY_2019_1_12 || UNITY_2019_1_13 || UNITY_2019_1_14 || UNITY_2019_2_OR_NEWER
#define UNITY_2019_1_9_OR_NEWER
#endif
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text;
using PackageInfo = UnityEditor.PackageManager.UI.PackageInfo;
using UnityEditor.PackageManager;
// using UnityEditor.PackageManager.UI.InternalBridge;
// using Debug = UnityEditor.PackageManager.UI.InternalBridge.Debug;
#if !UNITY_2019_1_9_OR_NEWER
using Semver;
#endif

namespace Coffee.PackageManager.UI
{
    [Serializable]
    public class AvailableVersion : IEquatable<AvailableVersion>
    {
        public string packageName = "";
        public string version = "";
        public string refName = "";
        public string repoUrl = "";

        public string refNameText { get { return version == refName ? version : version + " - " + refName; } }

        bool IEquatable<AvailableVersion>.Equals(AvailableVersion other)
        {
            return other != null
                && packageName == other.packageName
                && version == other.version
                && repoUrl == other.repoUrl
                && refName == other.refName;
        }

        public override int GetHashCode()
        {
            return packageName.GetHashCode()
                + version.GetHashCode()
                + repoUrl.GetHashCode()
                + refName.GetHashCode();
        }
#if UNITY_2019_2_OR_NEWER

        internal UpmPackageVersion ToPackageVersion(UpmPackageVersion baseInfo)
        {
            Debug.Log(kHeader, "[UpdatePackageVersions] version = {0}", ver);
            var splited = ver.Split(',');
            var semver = SemVersion.Parse(this.refNameText);

            var newPInfo = JsonUtility.FromJson<PackageInfo>(JsonUtility.ToJson(baseInfo));
            newPInfo.m_Version = this.version;
            newPInfo.m_Git = new GitInfo("", this.refName);

            var p = new UpmPackageVersion(newPInfo, false, semver, pInfo.displayName);

            // Update tag.
            PackageTag tag = PackageTag.Git | PackageTag.Installable | PackageTag.Removable;
            if ((semver.Major == 0 && string.IsNullOrEmpty(semver.Prerelease)) ||
                PackageTag.Preview.ToString().Equals(semver.Prerelease.Split('.')[0], StringComparison.InvariantCultureIgnoreCase))
                tag |= PackageTag.Preview;

            else if (semver.IsRelease())
                tag |= PackageTag.Release;

            p.m_Tag = tag;
            p.m_IsFullyFetched = true;
            p.m_PackageId = string.Format("{0}@{1}#{2}", packageName, repoUrl, this.refName);
            return p;
        }
#else
        internal PackageInfo ToPackageVersion(PackageInfo baseInfo)
        {
            var newPInfo = JsonUtility.FromJson<PackageInfo>(JsonUtility.ToJson(baseInfo));

            newPInfo.Version = SemVersion.Parse(this.refNameText);
#if UNITY_2019_2_OR_NEWER
            newPInfo.IsInstalled = false;
#else
            newPInfo.IsCurrent = false;
#endif
            newPInfo.IsVerified = false;
            newPInfo.Origin = (PackageSource)99;
            newPInfo.Info = baseInfo.Info;
            newPInfo.PackageId = string.Format("{0}@{1}", newPInfo.Name, this.refName);
            return newPInfo;
        }
#endif
    }

    public class AvailableVersions : ScriptableSingleton<AvailableVersions>
    {
        const string kCacheDir = "Library/UpmGitExtension";
        const string kPackageDir = kCacheDir + "/packages";
        const string kResultDir = kCacheDir + "/results";
        const string kHeader = "<b><color=#c7634c>[AvailableVersions]</color></b> ";
        const string kGetVersionsJs = "Packages/com.coffee.upm-git-extension/Editor/Commands/get-available-versions.js";

        public AvailableVersion[] versions = new AvailableVersion[0];

        public static event Action OnChanged = () => { };

        [Serializable]
        public class ResultInfo
        {
            public AvailableVersion[] versions;
        }

        [MenuItem("UpmGitExtensions/Clear Cached Versions")]
        public static void ClearAll()
        {
            Debug.Log(kHeader, "Clear cached versions");
            instance.versions = new AvailableVersion[0];

            if (Directory.Exists(kPackageDir))
                Directory.Delete(kPackageDir, true);
        }

        public static void Clear(string packageName = null, string repoUrl = null)
        {
            instance.versions = instance.versions
                .Where(x => string.IsNullOrEmpty(packageName) || x.packageName != packageName)
                .Where(x => string.IsNullOrEmpty(repoUrl) || x.repoUrl != repoUrl)
                .ToArray();
        }

        public static IEnumerable<AvailableVersion> GetVersions(string packageName = null, string repoUrl = null)
        {
            return instance.versions
                .Where(x => string.IsNullOrEmpty(packageName) || x.packageName == packageName)
                .Where(x => string.IsNullOrEmpty(repoUrl) || x.repoUrl == repoUrl);
        }

        [MenuItem("UpmGitExtensions/Dump Cached Versions")]
        public static void Dump()
        {
            var sb = new StringBuilder("[AvailableVersions] Dump:\n");
            foreach(var v in instance.versions.OrderBy(x=>x.packageName).ThenBy(x=>x.version))
            {
                sb.AppendLine(JsonUtility.ToJson(v));
            }
            Debug.Log(kHeader, sb);
        }

        public static void AddVersions(IEnumerable<AvailableVersion> add)
        {
            if (add == null || !add.Any())
                return;

            var length = instance.versions.Length;
            var versions = instance.versions
                .Union(add)
                .ToArray();

            if (versions.Length != length)
            {
                Debug.Log(kHeader, "<b>DIRTY</b>");
                instance.versions = versions;
                OnChanged();
            }
            else
                Debug.Log(kHeader, "NOT DIRTY");
        }

        public static void UpdateAvailableVersions(string packageName = "all", string repoUrl = "", Action<bool> callback = null)
        {
            var unity = Application.unityVersion;
#if UNITY_EDITOR_WIN
            var node = Path.Combine(EditorApplication.applicationContentsPath, "Tools/nodejs/node.exe").Replace('/', '\\');
#else
            var node = Path.Combine(EditorApplication.applicationContentsPath, "Tools/nodejs/bin/node");
#endif

            var psi = new ProcessStartInfo()
            {
                FileName = node,
                Arguments = string.Format("\"{0}\" {1} {2} {3}", kGetVersionsJs, packageName, repoUrl, unity)
            };
            Debug.Log(kHeader, "{0} {1}", psi.FileName, psi.Arguments);

            var p = new UnityEditor.Utils.Program(psi);
            if (callback != null)
                p.Start((_, __) => callback(p._process.ExitCode == 0));
            else
                p.Start();
        }

        static void OnResultCreated(object o, FileSystemEventArgs e)
        {
            if (Path.GetExtension(e.Name) == ".json")
                OnResultCreated(e.Name);
        }

        static void OnResultCreated(string file)
        {
            try
            {
                var info = JsonUtility.FromJson<ResultInfo>(File.ReadAllText(file));
                EditorApplication.delayCall += () => AvailableVersions.AddVersions(info.versions);
            }
            finally
            {
                File.Delete(file);
            }
        }

        [InitializeOnLoadMethod]
        static void WatchResultJson()
        {
#if !UNITY_EDITOR_WIN
            Environment.SetEnvironmentVariable("MONO_MANAGED_WATCHER", "enabled");
#endif
            if (!Directory.Exists(kResultDir))
                Directory.CreateDirectory(kResultDir);

            foreach (var file in Directory.GetFiles(kResultDir, "*.json"))
                OnResultCreated(file);

            var watcher = new FileSystemWatcher()
            {
                Path = kResultDir,
                NotifyFilter = NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };

            watcher.Created += OnResultCreated;
        }
    }
}
#endif // This line is added by Open Sesame Portable. DO NOT remov manually.