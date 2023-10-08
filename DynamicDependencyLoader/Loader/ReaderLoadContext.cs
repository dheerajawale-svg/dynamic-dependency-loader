using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace DynamicDependencyLoader.Loader
{
    /// <summary>
    /// An implementation of <see cref="AssemblyLoadContext" /> which attempts to load managed and native
    /// binaries at runtime immitating some of the behaviors of corehost.
    /// </summary>
    internal class ReaderLoadContext : AssemblyLoadContext
    {
        #region Members
        
        private readonly string _basePath;
        private readonly ICollection<string> _defaultAssemblies;
        private readonly IReadOnlyCollection<string> _additionalProbingPaths;
        private readonly string[] _resourceRoots;
        private readonly AssemblyLoadContext _defaultLoadContext;
        private readonly AssemblyDependencyResolver _dependencyResolver;

        #endregion

        public ReaderLoadContext(string mainAssemblyPath,
                    IReadOnlyCollection<string> defaultAssemblies,
                    IReadOnlyCollection<string> additionalProbingPaths,
                    IReadOnlyCollection<string> resourceProbingPaths,
                    AssemblyLoadContext defaultLoadContext)
            : base(Path.GetFileNameWithoutExtension(mainAssemblyPath))
        {
            if (resourceProbingPaths == null)
            {
                throw new ArgumentNullException(nameof(resourceProbingPaths));
            }

            _dependencyResolver = new AssemblyDependencyResolver(mainAssemblyPath);
            _basePath = Path.GetDirectoryName(mainAssemblyPath) ?? throw new ArgumentException(nameof(mainAssemblyPath));            
            _defaultAssemblies = defaultAssemblies != null ? defaultAssemblies.ToList() : throw new ArgumentNullException(nameof(defaultAssemblies));            
            _additionalProbingPaths = additionalProbingPaths ?? throw new ArgumentNullException(nameof(additionalProbingPaths));
            _defaultLoadContext = defaultLoadContext;

            _resourceRoots = new[] { _basePath }
                                .Concat(resourceProbingPaths)
                                .ToArray();            
        }

        /// <summary>
        /// Load an assembly.
        /// </summary>
        /// <param name="assemblyName"></param>
        /// <returns></returns>
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == null)
            {
                // not sure how to handle this case. It's technically possible.
                return null;
            }

            if (_defaultAssemblies.Contains(assemblyName.Name))
            {
                // If default context is preferred, check first for types in the default context unless the dependency has been declared as private
                try
                {
                    var defaultAssembly = _defaultLoadContext.LoadFromAssemblyName(assemblyName);
                    if (defaultAssembly != null)
                    {
                        // Older versions used to return null here such that returned assembly would be resolved from the default ALC.
                        // However, with the addition of custom default ALCs, the Default ALC may not be the user's chosen ALC when
                        // this context was built. As such, we simply return the Assembly from the user's chosen default load context.
                        return defaultAssembly;
                    }
                }
                catch
                {
                    // Swallow errors in loading from the default context
                }
            }

            var resolvedPath = _dependencyResolver.ResolveAssemblyToPath(assemblyName);
            if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
            {
                return LoadAssemblyFromFilePath(resolvedPath);
            }

            // Resource assembly binding does not use the TPA. Instead, it probes PLATFORM_RESOURCE_ROOTS (a list of folders)
            // for $folder/$culture/$assemblyName.dll
            // See https://github.com/dotnet/coreclr/blob/3fca50a36e62a7433d7601d805d38de6baee7951/src/binder/assemblybinder.cpp#L1232-L1290

            if (!string.IsNullOrEmpty(assemblyName.CultureName) && !string.Equals("neutral", assemblyName.CultureName))
            {
                foreach (var resourceRoot in _resourceRoots)
                {
                    var resourcePath = Path.Combine(resourceRoot, assemblyName.CultureName, assemblyName.Name + ".dll");
                    if (File.Exists(resourcePath))
                    {
                        return LoadAssemblyFromFilePath(resourcePath);
                    }
                }

                return null;
            }

            // if an assembly was not listed in the list of known assemblies,
            // fallback to the load context base directory
            var dllName = assemblyName.Name + ".dll";
            foreach (var probingPath in _additionalProbingPaths.Prepend(_basePath))
            {
                var localFile = Path.Combine(probingPath, dllName);
                if (File.Exists(localFile))
                {
                    return LoadAssemblyFromFilePath(localFile);
                }
            }

            return null;
        }

        public Assembly LoadAssemblyFromFilePath(string path)
        {
            return LoadFromAssemblyPath(path);
        }

        #region For UnManaged DLL - Microsoft.Data.SqlClient.SNI.dll

        /// <summary>
        /// Loads the unmanaged binary using configured list of native libraries.
        /// </summary>
        /// <param name="unmanagedDllName"></param>
        /// <returns></returns>
        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var resolvedPath = _dependencyResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
            {
                return LoadUnmanagedDllFromResolvedPath(resolvedPath, normalizePath: false);
            }
            
            return base.LoadUnmanagedDll(unmanagedDllName);
        }

        private IntPtr LoadUnmanagedDllFromResolvedPath(string unmanagedDllPath, bool normalizePath = true)
        {
            if (normalizePath)
            {
                unmanagedDllPath = Path.GetFullPath(unmanagedDllPath);
            }

            return LoadUnmanagedDllFromPath(unmanagedDllPath);
        }

        #endregion
    }
}
