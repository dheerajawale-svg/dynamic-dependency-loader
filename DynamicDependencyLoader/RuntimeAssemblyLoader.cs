using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using DynamicDependencyLoader.Loader;

namespace DynamicDependencyLoader
{
    /// <summary>
    /// This loader attempts to load binaries for execution (both managed assemblies and native libraries)
    /// in the same way that .NET Core would if they were originally part of the .NET Core application.
    /// <para>
    /// This loader reads configuration files produced by .NET Core (.deps.json and runtimeconfig.json)
    /// These files describe a list of .dlls and a set of dependencies.
    /// The loader searches the Adapter path, as well as any additionally specified paths, for binaries
    /// which satisfy the Adapter's requirements.
    /// </para>
    /// </summary>
    public class RuntimeAssemblyLoader : IDisposable
    {
        /// <summary>
        /// Create a plugin loader for an assembly file.
        /// </summary>
        /// <param name="assemblyFile">The file path to the main assembly for the plugin.</param>
        /// <param name="sharedTypes">
        /// <para>
        /// A list of types which should be shared between the host and the plugin.
        /// </para>
        /// <para>
        /// <seealso href="https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md">
        /// https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md
        /// </seealso>
        /// </para>
        /// </param>
        /// <returns>A loader.</returns>
        public static RuntimeAssemblyLoader CreateFromAssemblyFile(string assemblyFile, Type[] sharedTypes)
            => CreateFromAssemblyFile(assemblyFile, sharedTypes, _ => { });

        /// <summary>
        /// Create a plugin loader for an assembly file.
        /// </summary>
        /// <param name="assemblyFile">The file path to the main assembly for the plugin.</param>
        /// <param name="sharedTypes">
        /// <para>
        /// A list of types which should be shared between the host and the plugin.
        /// </para>
        /// <para>
        /// <seealso href="https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md">
        /// https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md
        /// </seealso>
        /// </para>
        /// </param>
        /// <param name="configure">A function which can be used to configure advanced options for the plugin loader.</param>
        /// <returns>A loader.</returns>
        public static RuntimeAssemblyLoader CreateFromAssemblyFile(string assemblyFile, Type[] sharedTypes, Action<RuntimeAssemblyConfig> configure)
        {
            return CreateFromAssemblyFile(assemblyFile,
                    config =>
                    {
                        if (sharedTypes != null)
                        {
                            var uniqueAssemblies = new HashSet<Assembly>();
                            foreach (var type in sharedTypes)
                            {
                                uniqueAssemblies.Add(type.Assembly);
                            }

                            foreach (var assembly in uniqueAssemblies)
                            {
                                config.SharedAssemblies.Add(assembly.GetName());
                            }
                        }
                        configure(config);
                    });
        }

        /// <summary>
        /// Create a plugin loader for an assembly file.
        /// </summary>
        /// <param name="assemblyFile">The file path to the main assembly for the plugin.</param>
        /// <param name="configure">A function which can be used to configure advanced options for the plugin loader.</param>
        /// <returns>A loader.</returns>
        public static RuntimeAssemblyLoader CreateFromAssemblyFile(string assemblyFile, Action<RuntimeAssemblyConfig> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var config = new RuntimeAssemblyConfig(assemblyFile);
            configure(config);
            return new RuntimeAssemblyLoader(config);
        }

        private readonly RuntimeAssemblyConfig _config;
        private ReaderLoadContext _context;
        private readonly AssemblyLoadContextBuilder _contextBuilder;
        private volatile bool _disposed;

        /// <summary>
        /// Initialize an instance of <see cref="RuntimeAssemblyLoader" />
        /// </summary>
        /// <param name="config">The configuration for the plugin.</param>
        public RuntimeAssemblyLoader(RuntimeAssemblyConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _contextBuilder = CreateLoadContextBuilder(config);
            _context = (ReaderLoadContext)_contextBuilder.Build();            
        }

        /// <summary>
        /// True when this plugin is capable of being unloaded.
        /// </summary>
        public bool IsUnloadable => _context.IsCollectible;

        /// <summary>
        /// Load the main assembly for the plugin.
        /// </summary>
        public Assembly LoadDefaultAssembly()
        {
            EnsureNotDisposed();
            return _context.LoadAssemblyFromFilePath(_config.MainAssemblyPath);
        }

        private static AssemblyLoadContextBuilder CreateLoadContextBuilder(RuntimeAssemblyConfig config)
        {
            var builder = new AssemblyLoadContextBuilder();

            builder.SetMainAssemblyPath(config.MainAssemblyPath);
            builder.SetDefaultContext(AssemblyLoadContext.Default);

            foreach (var assemblyName in config.SharedAssemblies)
            {
                builder.PreferDefaultLoadContextAssembly(assemblyName);
            }

            var baseDir = Path.GetDirectoryName(config.MainAssemblyPath);
            //var assemblyFileName = Path.GetFileNameWithoutExtension(config.MainAssemblyPath);

            if (baseDir == null)
            {
                throw new InvalidOperationException("Could not determine which directory to watch. "
                + "Please set MainAssemblyPath to an absolute path so its parent directory can be discovered.");
            }

            return builder;
        }

        /// <summary>
        /// Disposes the plugin loader. This only does something if <see cref="IsUnloadable" /> is true.
        /// When true, this will unload assemblies which which were loaded during the lifetime
        /// of the plugin.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_context.IsCollectible)
            {
                _context.Unload();
            }
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RuntimeAssemblyLoader));
            }
        }
    }

    public class RuntimeAssemblyConfig
    {
        /// <summary>
        /// Initializes a new instance of <see cref="RuntimeAssemblyConfig" />
        /// </summary>
        /// <param name="mainAssemblyPath">The full file path to the main assembly for the plugin.</param>
        public RuntimeAssemblyConfig(string mainAssemblyPath)
        {
            if (string.IsNullOrEmpty(mainAssemblyPath))
            {
                throw new ArgumentException("Value must be null or not empty", nameof(mainAssemblyPath));
            }

            if (!Path.IsPathRooted(mainAssemblyPath))
            {
                throw new ArgumentException("Value must be an absolute file path", nameof(mainAssemblyPath));
            }

            MainAssemblyPath = mainAssemblyPath;
        }

        /// <summary>
        /// The file path to the main assembly.
        /// </summary>
        public string MainAssemblyPath { get; }

        /// <summary>
        /// A list of assemblies which should be unified between the host and the plugin.
        /// </summary>
        /// <seealso href="https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md">
        /// https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md
        /// </seealso>
        public ICollection<AssemblyName> SharedAssemblies { get; protected set; } = new List<AssemblyName>();
    }
}
