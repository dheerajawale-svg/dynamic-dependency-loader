using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace DynamicDependencyLoader.Loader
{
    /// <summary>
    /// A builder for creating an instance of <see cref="AssemblyLoadContext" />.
    /// </summary>
    public class AssemblyLoadContextBuilder
    {
        private readonly List<string> _additionalProbingPaths = new();
        private readonly List<string> _resourceProbingPaths = new();
        private readonly List<string> _resourceProbingSubpaths = new();
        private readonly HashSet<string> _defaultAssemblies = new(StringComparer.Ordinal);
        private AssemblyLoadContext _defaultLoadContext = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly()) ?? AssemblyLoadContext.Default;
        private string? _mainAssemblyPath;
        
        /// <summary>
        /// Creates an assembly load context using settings specified on the builder.
        /// </summary>
        /// <returns>A new ManagedLoadContext.</returns>
        public AssemblyLoadContext Build()
        {
            var resourceProbingPaths = new List<string>(_resourceProbingPaths);
            foreach (var additionalPath in _additionalProbingPaths)
            {
                foreach (var subPath in _resourceProbingSubpaths)
                {
                    resourceProbingPaths.Add(Path.Combine(additionalPath, subPath));
                }
            }

            if (_mainAssemblyPath == null)
            {
                throw new InvalidOperationException($"Missing required property. You must call '{nameof(SetMainAssemblyPath)}' to configure the default assembly.");
            }

            return new ReaderLoadContext(_mainAssemblyPath,
                    _defaultAssemblies,
                    _additionalProbingPaths, resourceProbingPaths,
                    _defaultLoadContext);
        }

        /// <summary>
        /// Set the file path to the main assembly for the context. This is used as the starting point for loading
        /// other assemblies.
        /// </summary>
        public AssemblyLoadContextBuilder SetMainAssemblyPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Argument must not be null or empty.", nameof(path));
            }

            if (!Path.IsPathRooted(path))
            {
                throw new ArgumentException("Argument must be a full path.", nameof(path));
            }

            _mainAssemblyPath = path;
            return this;
        }

        /// <summary>
        /// Replaces the default <see cref="AssemblyLoadContext"/> used by the <see cref="AssemblyLoadContextBuilder"/>.
        /// </summary>
        public AssemblyLoadContextBuilder SetDefaultContext(AssemblyLoadContext context)
        {
            _defaultLoadContext = context ?? throw new ArgumentException($"Bad Argument: AssemblyLoadContext in {nameof(AssemblyLoadContextBuilder)}.{nameof(SetDefaultContext)} is null.");
            return this;
        }

        /// <summary>
        /// Instructs the load context to first attempt to load assemblies by this name from the default app context, even
        /// if other assemblies in this load context express a dependency on a higher or lower version.
        /// Use this when you need to exchange types created from within the load context with other contexts
        /// or the default app context.
        /// </summary>
        /// <param name="assemblyName">The name of the assembly.</param>
        /// <returns>The builder.</returns>
        public AssemblyLoadContextBuilder PreferDefaultLoadContextAssembly(AssemblyName assemblyName)
        {
            var names = new Queue<AssemblyName>();
            names.Enqueue(assemblyName);
            while (names.TryDequeue(out var name))
            {
                if (name.Name == null || _defaultAssemblies.Contains(name.Name))
                {
                    // base cases
                    continue;
                }

                _defaultAssemblies.Add(name.Name);

                var assembly = _defaultLoadContext.LoadFromAssemblyName(name);

                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    names.Enqueue(reference);
                }
            }

            return this;
        }

        /// <summary>
        /// Add a <paramref name="path"/> that should be used to search for native and managed libraries.
        /// </summary>
        public AssemblyLoadContextBuilder AddProbingPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Value must not be null or empty.", nameof(path));
            }

            if (!Path.IsPathRooted(path))
            {
                throw new ArgumentException("Argument must be a full path.", nameof(path));
            }

            _additionalProbingPaths.Add(path);
            return this;
        }
    }
}
