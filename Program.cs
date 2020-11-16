using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using HtmlAgilityPack;

namespace GithubProjectsMigrator
{
    class Program
    {
        private static HtmlDocument LoadDoc(string url)
        {
            var htmlWeb = new HtmlWeb();
            return htmlWeb.Load(url);
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Enter origin account nickname");
            string originName = Console.ReadLine();
            string repositoriesPage = $"https://github.com/{originName}?tab=repositories";
            List<string> projects = new List<string>();
            int page = 0;
            while (true)
            {
                Console.WriteLine($"Loading page {page++}");
                var document = LoadDoc(repositoriesPage);
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

            File.WriteAllLines("projects.txt", projects);
            EditWaitProjects();
            var useProjects = File.ReadAllLines("projects.txt");
            File.Delete("projects.txt");


            Console.WriteLine("Enter destination account nickname");
            string destinationName = Console.ReadLine();

            foreach (var project in useProjects)
            {
                StringBuilder outputBuilder = new StringBuilder();


                ProcessStartInfo startInfo = new ProcessStartInfo("cmd")
                {
                    RedirectStandardInput = true, 
                    RedirectStandardOutput = true, 
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                var process = new Process {StartInfo = startInfo};
                process.OutputDataReceived += (sender, eventArgs) => outputBuilder.AppendLine(eventArgs.Data);
                process.Start();
                process.BeginOutputReadLine();

                process.StandardInput.WriteLine($"cd \"{Environment.CurrentDirectory}\"");
                process.StandardInput.WriteLine($"git clone --bare https://github.com/{originName}/{project}");
                process.StandardInput.WriteLine($"cd {project}.git");
                process.StandardInput.WriteLine($"hub create --remote-name \"upstream\" {destinationName}/{project}");
                process.StandardInput.WriteLine(
                    $"git push --mirror https://github.com/{destinationName}/{project}.git");
                process.StandardInput.WriteLine($"cd ..");
                process.StandardInput.WriteLine($"rmdir /Q /S {project}.git");
                process.StandardInput.WriteLine($"hub delete {originName}/{project}");
                process.StandardInput.WriteLine($"exit");

                process.WaitForExit();

                // for debug purposes
                string output = outputBuilder.ToString();
            }
        }

        private static void EditWaitProjects()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo("notepad.exe", Path.GetFullPath("projects.txt"));
            var process = Process.Start(startInfo);
            process.WaitForExit();
        }
    }
}