﻿using StorybrewCommon.Scripting;
using StorybrewEditor.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace StorybrewEditor.Scripting
{
    public class ScriptManager<TScript> : IDisposable
        where TScript : Script
    {
        private string scriptsNamespace;
        private string scriptsSourcePath;
        private string commonScriptsPath;
        private string scriptsLibraryPath;
        private string compiledScriptsPath;
        private string[] referencedAssemblies;

        private FileSystemWatcher scriptWatcher;
        private FileSystemWatcher libraryWatcher;
        private ThrottledActionScheduler scheduler = new ThrottledActionScheduler();
        private Dictionary<string, ScriptContainer<TScript>> scriptContainers = new Dictionary<string, ScriptContainer<TScript>>();

        public string ScriptsPath => scriptsSourcePath;

        public ScriptManager(string scriptsNamespace, string scriptsSourcePath, string commonScriptsPath, string scriptsLibraryPath, string compiledScriptsPath, params string[] referencedAssemblies)
        {
            this.scriptsNamespace = scriptsNamespace;
            this.scriptsSourcePath = scriptsSourcePath;
            this.commonScriptsPath = commonScriptsPath;
            this.scriptsLibraryPath = scriptsLibraryPath;
            this.referencedAssemblies = referencedAssemblies;
            this.compiledScriptsPath = compiledScriptsPath;

            scriptWatcher = new FileSystemWatcher()
            {
                Filter = "*.cs",
                Path = scriptsSourcePath,
                IncludeSubdirectories = false,
            };
            scriptWatcher.Created += scriptWatcher_Changed;
            scriptWatcher.Changed += scriptWatcher_Changed;
            scriptWatcher.Renamed += scriptWatcher_Changed;
            scriptWatcher.Error += (sender, e) => Trace.WriteLine($"Watcher error (script): {e.GetException()}");
            scriptWatcher.EnableRaisingEvents = true;

            libraryWatcher = new FileSystemWatcher()
            {
                Filter = "*.cs",
                Path = scriptsLibraryPath,
                IncludeSubdirectories = true,
            };
            libraryWatcher.Created += libraryWatcher_Changed;
            libraryWatcher.Changed += libraryWatcher_Changed;
            libraryWatcher.Renamed += libraryWatcher_Changed;
            libraryWatcher.Error += (sender, e) => Trace.WriteLine($"Watcher error (library): {e.GetException()}");
            libraryWatcher.EnableRaisingEvents = true;
        }

        public ScriptContainer<TScript> Get(string scriptName)
        {
            if (disposedValue) throw new ObjectDisposedException(nameof(ScriptManager<TScript>));

            ScriptContainer<TScript> scriptContainer;
            if (scriptContainers.TryGetValue(scriptName, out scriptContainer))
                return scriptContainer;

            var scriptTypeName = $"{scriptsNamespace}.{scriptName}";
            var sourcePath = Path.Combine(scriptsSourcePath, $"{scriptName}.cs");

            if (commonScriptsPath != null && !File.Exists(sourcePath))
            {
                var commonSourcePath = Path.Combine(commonScriptsPath, $"{scriptName}.cs");
                if (File.Exists(commonSourcePath))
                {
                    File.Copy(commonSourcePath, sourcePath);
                    File.SetAttributes(sourcePath, File.GetAttributes(sourcePath) & ~FileAttributes.ReadOnly);
                }
            }

            scriptContainer = new ScriptContainerAppDomain<TScript>(this, scriptTypeName, sourcePath, scriptsLibraryPath, compiledScriptsPath, referencedAssemblies);
            //scriptContainer = new ScriptContainerProcess<TScript>(this, scriptTypeName, sourcePath, scriptsLibraryPath, compiledScriptsPath, referencedAssemblies);
            scriptContainers.Add(scriptName, scriptContainer);
            return scriptContainer;
        }

        public IEnumerable<string> GetScriptNames()
        {
            var projectScriptNames = new List<string>();
            foreach (var scriptPath in Directory.GetFiles(scriptsSourcePath, "*.cs", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(scriptPath);
                projectScriptNames.Add(name);
                yield return name;
            }
            foreach (var scriptPath in Directory.GetFiles(commonScriptsPath, "*.cs", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(scriptPath);
                if (!projectScriptNames.Contains(name))
                    yield return name;
            }
        }

        private void scriptWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            scheduler?.Schedule(e.FullPath, (key) =>
            {
                if (disposedValue)
                    return;

                var scriptName = Path.GetFileNameWithoutExtension(e.Name);

                ScriptContainer<TScript> container;
                if (scriptContainers.TryGetValue(scriptName, out container))
                {
                    Trace.WriteLine($"Watched script file {e.ChangeType.ToString().ToLowerInvariant()}: {e.FullPath}");
                    container.ReloadScript();
                }
            });
        }

        private void libraryWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            scheduler?.Schedule(e.FullPath, (key) =>
            {
                if (disposedValue)
                    return;

                Trace.WriteLine($"Watched library file {e.ChangeType.ToString().ToLowerInvariant()}: {e.FullPath}");
                foreach (var container in scriptContainers.Values)
                    container.ReloadScript();
            });
        }

        #region IDisposable Support

        private bool disposedValue = false;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    scriptWatcher.Dispose();
                    libraryWatcher.Dispose();
                    foreach (var entry in scriptContainers)
                        entry.Value.Dispose();
                }
                scheduler = null;
                scriptWatcher = null;
                scriptContainers = null;

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }
}
