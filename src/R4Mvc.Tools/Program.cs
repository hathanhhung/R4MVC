﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.Setup.Configuration;
using R4Mvc.Tools.Locators;
using R4Mvc.Tools.Services;

namespace R4Mvc.Tools
{
    class Program
    {
        static void Main(string[] args) 
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Looking for project...");
                var projFiles = Directory.GetFiles(Environment.CurrentDirectory, "*.csproj");
                switch (projFiles.Length)
                {
                    case 1:
                        args = projFiles;
                        break;

                    case 0:
                        Console.WriteLine("Project path not found. Pass one as a parameter or run this within the project directory.");
                        return;
                    default:
                        Console.WriteLine("More than one project file found. Aborting, just to be safe!");
                        return;
                }
            }

            var projectPath = Path.IsPathRooted(args[0]) ? args[0] : Path.Combine(Environment.CurrentDirectory, args[0]);
            Console.WriteLine($"Project path: {projectPath}");

            if (!File.Exists(projectPath))
            {
                Console.WriteLine("Project file doesn't exist");
                return;
            }

            var configuration = LoadConfiguration(args.Skip(1).ToArray(), projectPath);

            var services = new ServiceCollection();
            ConfigureServices(services, configuration);

            var serviceProvider = services.BuildServiceProvider();

            new Program().Run(projectPath, serviceProvider).Wait();
        }

        public async Task Run(string projectPath, IServiceProvider serviceProvider)
        {
            ConfigureMSBuild();

            var workspace = MSBuildWorkspace.Create();
            var generator = serviceProvider.GetService<R4MvcGenerator>();
            var settings = serviceProvider.GetService<IOptions<Settings>>().Value;

            var project = await workspace.OpenProjectAsync(projectPath);
            if (workspace.Diagnostics.Count > 0)
            {
                foreach (var diag in workspace.Diagnostics)
                    Console.Error.WriteLine($"  {diag.Kind}: {diag.Message}");
                return;
            }

            var compilation = await project.GetCompilationAsync() as CSharpCompilation;
            generator.Generate(compilation, Path.GetDirectoryName(project.FilePath));
        }

        private void ConfigureMSBuild()
        {
            var query = new SetupConfiguration();
            var query2 = (ISetupConfiguration2)query;

            try
            {
                if (query2.GetInstanceForCurrentProcess() is ISetupInstance2 instance)
                {
                    Environment.SetEnvironmentVariable("VSINSTALLDIR", instance.GetInstallationPath());
                    Environment.SetEnvironmentVariable("VisualStudioVersion", @"15.0");
                    return;
                }
            }
            catch { }

            var instances = new ISetupInstance[1];
            var e = query2.EnumAllInstances();
            int fetched;
            do
            {
                e.Next(1, instances, out fetched);
                if (fetched > 0)
                {
                    var instance = instances[0] as ISetupInstance2;
                    if (instance.GetInstallationVersion().StartsWith("15."))
                    {
                        Environment.SetEnvironmentVariable("VSINSTALLDIR", instance.GetInstallationPath());
                        Environment.SetEnvironmentVariable("VisualStudioVersion", @"15.0");
                        return;
                    }
                }
            }
            while (fetched > 0);
        }

        static IConfigurationRoot LoadConfiguration(string[] args, string projectPath)
        {
            return new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(Path.GetDirectoryName(projectPath), "r4mvc.json"), optional: true)
                .AddCommandLine(args)
                .Build();
        }

        static void ConfigureServices(IServiceCollection services, IConfigurationRoot configuration)
        {
            services.AddOptions();
            services.Configure<Settings>(configuration);

            services.AddSingleton(typeof(IEnumerable<IViewLocator>), new[] { new DefaultRazorViewLocator() });
            services.AddSingleton(typeof(IEnumerable<IStaticFileLocator>), new[] { new DefaultStaticFileLocator() });
            services.AddTransient<IViewLocatorService, ViewLocatorService>();
            services.AddTransient<IStaticFileGeneratorService, StaticFileGeneratorService>();
            services.AddTransient<IControllerRewriterService, ControllerRewriterService>();
            services.AddTransient<IControllerGeneratorService, ControllerGeneratorService>();
            services.AddTransient<IFilePersistService, FilePersistService>();
            services.AddTransient<R4MvcGenerator, R4MvcGenerator>();
        }
    }
}
