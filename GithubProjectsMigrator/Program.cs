using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using HtmlAgilityPack;

namespace GithubProjectsMigrator
{
    class Program
    {
        private static (HtmlDocument document, bool errored) LoadDoc(string url)
        {
            var htmlWeb = new HtmlWeb();
            return (htmlWeb.Load(url), htmlWeb.StatusCode != HttpStatusCode.OK);
        }

        private static bool IsUserExist(string nickname)
        {
            string url = $"https://github.com/{nickname}";
            var (document, errored) = LoadDoc(url);
            return !errored;
        }

        private static List<string> RequestRepositoryProjects(string nickname)
        {
            string repositoriesPage = $"https://github.com/{nickname}?tab=repositories";
            List<string> projects = new List<string>();

            int page = 0;
            while (true)
            {
                Console.WriteLine($"Loading page {page++}");

                var (document, _) = LoadDoc(repositoriesPage);

                var repositoryNamesANodes = document.DocumentNode.SelectNodes("//a[@itemprop='name codeRepository']");

                var repositoryNames = repositoryNamesANodes.Select(t => t.InnerText.Trim('\n', ' '));
                projects.AddRange(repositoryNames);

                var paginationANodes = document.DocumentNode.SelectNodes("//div[@data-test-selector='pagination']/a");

                if (paginationANodes.Count == 2)
                {
                    repositoriesPage = paginationANodes[1].Attributes["href"].Value;
                }
                else
                {
                    if (paginationANodes[0].InnerText == "Next")
                    {
                        repositoriesPage = paginationANodes[0].Attributes["href"].Value;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            Console.WriteLine($"Loaded {page} pages with {projects.Count} projects");

            return projects;
        }

        private static void ExecuteCmd(IEnumerable<string> commands)
        {
            StringBuilder outputBuilder = new StringBuilder();

            ProcessStartInfo startInfo = new ProcessStartInfo("cmd")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (sender, eventArgs) => outputBuilder.AppendLine(eventArgs.Data);
            process.Start();
            process.BeginOutputReadLine();

            foreach (var command in commands)
            {
                process.StandardInput.WriteLine(command);
            }

            // Force quit
            process.StandardInput.WriteLine("exit /c");

            process.StandardInput.Flush();
            process.StandardInput.Close();

            AutoResetEvent resetEvent = new AutoResetEvent(false);
            process.Exited += (sender, args) => { resetEvent.Set(); };
            resetEvent.WaitOne();

            // for debug purposes
            string output = outputBuilder.ToString();
        }

        private static void MigrateProjects(string origin, string destination, ICollection<string> projects)
        {
            foreach (var project in projects)
            {
                Console.WriteLine($"Migrating project '{project}'");

                ExecuteCmd(new []
                {
                    $"cd \"{Environment.CurrentDirectory}\"",
                    $"git clone --bare https://github.com/{origin}/{project}",
                    $"cd {project}.git",
                    $"hub create --remote-name \"upstream\" {destination}/{project}",
                    $"git push --mirror https://github.com/{destination}/{project}.git",
                    $"cd ..",
                    $"rmdir /Q /S {project}.git",
                    $"hub delete {origin}/{project}"
                });

                Console.WriteLine($"Migrating project '{project}' Done.");
            }
        }

        private static void TryRemoveRepositories(string origin, string destination, ICollection<string> projects)
        {
            Console.WriteLine("Do you want to delete origin repositories?(y): ");
            if (Console.ReadLine() == "y")
            {
                Console.WriteLine("Please, ensure everything is copied");

                ExecuteCmd(new []
                {
                    $"start \"\" \"https://www.github.com/{destination}/repositories\" /c"
                });

                Console.WriteLine("Confirm?(y): ");
                if (Console.ReadLine() == "y")
                {
                    foreach (var project in projects)
                    {
                        Console.WriteLine($"Removing project '{project}'");

                        ExecuteCmd(new []
                        {
                            $"hub delete -y {origin}/{project}"
                        });

                        Console.WriteLine($"Removing project '{project}' Done.");
                    }
                }
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Enter origin account nickname");

            string originName = Console.ReadLine();

            if (!IsUserExist(originName))
            {
                Console.WriteLine("Entered user doesn't exist.");
                return;
            }

            var projects = RequestRepositoryProjects(originName);

            if (projects.Count == 0)
            {
                Console.WriteLine("Found 0 projects for this origin");
                return;
            }

            File.WriteAllLines("projects.txt", projects);
            EditWaitProjects();
            var useProjects = File.ReadAllLines("projects.txt");
            File.Delete("projects.txt");


            Console.WriteLine("Enter destination account nickname");
            string destinationName = Console.ReadLine();

            if (!IsUserExist(destinationName))
            {
                Console.WriteLine("Entered user doesn't exist.");
                return;
            }

            MigrateProjects(originName, destinationName, useProjects);

            TryRemoveRepositories(originName, destinationName, useProjects);
        }

        private static void EditWaitProjects()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo("notepad.exe", Path.GetFullPath("projects.txt"));
            var process = Process.Start(startInfo);
            process.WaitForExit();
        }
    }
}