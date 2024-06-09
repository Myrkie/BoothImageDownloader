﻿using System.Collections.Concurrent;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.IO.Compression;
using System.Text;
using BoothDownloader.Configuration;
using BoothDownloader.Miscellaneous;
using BoothDownloader.Web;
using ShellProgressBar;

namespace BoothDownloader;

internal static class BoothDownloader
{

    internal static JsonConfig? Configextern;

    private static async Task<int> Main(string[] args)
    {
        Console.Title = $"BoothDownloader - V{typeof(BoothDownloader).Assembly.GetName().Version}";

        var rootCommand = new RootCommand("Booth Downloader");

        var configOption = new Option<string>(
            name: "--config",
            description: "Path to configuration file",
            getDefaultValue: () => "./BDConfig.json"
        );

        var boothOption = new Option<string?>(
            name: "--booth",
            description: "Booth ID/URL"
        );

        var outputDirectoryOption = new Option<string>(
            name: "--output-dir",
            description: "Output Directory",
            getDefaultValue: () => "./BoothDownloaderOut"
        );

        var maxRetriesOption = new Option<int>(
            name: "--max-retries",
            description: "maximum retries for downloading binary files",
            getDefaultValue: () => 3
        );

        rootCommand.AddGlobalOption(configOption);
        rootCommand.AddOption(boothOption);
        rootCommand.AddOption(outputDirectoryOption);
        rootCommand.AddOption(maxRetriesOption);

        var cancellationTokenValueSource = new CancellationTokenValueSource();

        rootCommand.SetHandler(async (configFile, boothId, outputDirectory, maxRetries, cancellationToken) =>
        {
            var config = new JsonConfig(configFile);
            Configextern = config;

            #region First Boot

            if (config.Config.FirstBoot)
            {
                Console.WriteLine("Please paste in your cookie from browser.\n");
                var cookie = Console.ReadLine();
                config.Config.Cookie = cookie!;
                config.Config.FirstBoot = false;
                config.Save();
                Console.WriteLine("Cookie set!\n");
            }

            #endregion


            var idFromArgument = true;
            if (boothId == null)
            {
                idFromArgument = false;
                Console.WriteLine("Enter the Booth ID or URL: ");
                boothId = Console.ReadLine();
            }

            #region Prep Booth Client

            var client = new BoothClient(config.Config);
            var hasValidCookie = await client.IsCookieValidAsync(cancellationToken);

            if (hasValidCookie)
            {
                Console.WriteLine("Cookie is valid! - file downloads will function.\n");
            }
            else
            {
                Console.WriteLine(
                    "Cookie is not valid file downloads will not function!\nImage downloads will still function\nUpdate your cookie in the config file.\n"
                );
                config.Config.Cookie = "";
                config.Save();
            }

            #endregion

            if (boothId?.Equals("https://accounts.booth.pm/orders", StringComparison.OrdinalIgnoreCase) == true || boothId?.Equals("orders", StringComparison.OrdinalIgnoreCase) == true || boothId?.Equals("order", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (hasValidCookie)
                {
                    Console.WriteLine("Downloading all Paid Orders!\n");
                    var list = await BoothOrders.OrdersLoopAsync(cancellationToken);
                    Console.WriteLine($"Orders to download: {list.Count}\nthis may be more than expected as this doesnt account for invalid or deleted items\n");

                    foreach (var items in list)
                    {
                        Console.WriteLine($"Downloading {items.Id}\n");
                        await MainParsingAsync(items.Id, outputDirectory, idFromArgument, config, client, hasValidCookie, maxRetries, cancellationToken);
                    }
                }
                else
                {
                    Console.WriteLine("Cannot download paid orders with invalid cookie.\n");
                    await Task.Delay(1500, cancellationToken);
                }
            }
            else if (boothId?.Equals("https://accounts.booth.pm/library", StringComparison.OrdinalIgnoreCase) == true || boothId?.Equals("libraries", StringComparison.OrdinalIgnoreCase) == true || boothId?.Equals("library", StringComparison.OrdinalIgnoreCase) == true)
            {

                if (hasValidCookie)
                {
                    Console.WriteLine("Downloading all Paid Library Items!\n");
                    var list = await BoothLibrary.LibraryLoopAsync(false, cancellationToken);
                    Console.WriteLine($"Library Items to download: {list.Count}\nthis may be more than expected as this doesnt account for invalid or deleted items\n");

                    foreach (var items in list)
                    {
                        Console.WriteLine($"Downloading {items.Id}\n");
                        await MainParsingAsync(items.Id, outputDirectory, idFromArgument, config, client, hasValidCookie, maxRetries, cancellationToken);
                    }
                }
                else
                {
                    Console.WriteLine("Cannot download paid library items with invalid cookie.\n");
                    await Task.Delay(1500, cancellationToken);
                }
            }
            else if (boothId?.Equals("https://accounts.booth.pm/library/gifts", StringComparison.OrdinalIgnoreCase) == true || boothId?.Equals("gifts", StringComparison.OrdinalIgnoreCase) == true || boothId?.Equals("gift", StringComparison.OrdinalIgnoreCase) == true)
            {

                if (hasValidCookie)
                {
                    Console.WriteLine("Downloading all Paid Gifts!\n");
                    var list = await BoothLibrary.LibraryLoopAsync(true, cancellationToken);
                    Console.WriteLine($"Gifts to download: {list.Count}\nthis may be more than expected as this doesnt account for invalid or deleted items\n");

                    foreach (var items in list)
                    {
                        Console.WriteLine($"Downloading {items.Id}\n");
                        await MainParsingAsync(items.Id, outputDirectory, idFromArgument, config, client, hasValidCookie, maxRetries, cancellationToken);
                    }
                }
                else
                {
                    Console.WriteLine("Cannot download paid gifts with invalid cookie.\n");
                    await Task.Delay(1500, cancellationToken);
                }
            }
            else
            {
                await MainParsingAsync(boothId, outputDirectory, idFromArgument, config, client, hasValidCookie, maxRetries, cancellationToken);
            }
        }, configOption, boothOption, outputDirectoryOption, maxRetriesOption, cancellationTokenValueSource);

        var commandLineBuilder = new CommandLineBuilder(rootCommand);
        var built = commandLineBuilder.Build();
        return await built.InvokeAsync(args);
    }

    private static async Task MainParsingAsync(string? boothId, string outputDirectory, bool idFromArgument, JsonConfig config, BoothClient client, bool hasValidCookie, int maxRetries, CancellationToken cancellationToken = default)
    {
        #region Prep Booth ID

        boothId = RegexStore.IdRegex.Match(boothId!)
            .Value;

        #endregion

        Console.WriteLine($"max-retries set to {maxRetries}");
        Console.WriteLine($"requested booth id {boothId}");

        #region Prep Folders

        if (boothId == "")
        {
            Console.WriteLine("Booth ID is invalid");
            return;
        }
        var outputDir = Directory.CreateDirectory(outputDirectory);
        if (Directory.Exists(Path.Combine(outputDir.ToString(), boothId)))
        {
            Console.WriteLine("Directory already exists. Deleting...");
            Directory.Delete(Path.Combine(outputDir.ToString(), boothId),
                true);
        }

        var entryDir = Directory.CreateDirectory(Path.Combine(outputDir.ToString(), boothId));
        var binaryDir = Directory.CreateDirectory(Path.Combine(entryDir.ToString(), "Binary"));

        #endregion

        #region Parse Booth Item Page

        string html;
        try
        {
            html = await client.GetItemPageAsync(boothId, cancellationToken);
        }
        catch (System.Net.WebException webException)
        {
            Console.WriteLine($"A Web-exception was thrown and will be ignored for this item, Does the page still exist?\n {webException}");
            Directory.Delete(Path.Combine(binaryDir.ToString(), boothId), true);
            return;
        }
        catch (Exception e)
        {
            Console.WriteLine($"An unexpected exception occurred\n {e}");
            return;
        }


        var imageCollection = RegexStore.ImageRegex.Matches(html)
            .Select(match => match.Value)
            .Where(url =>
                // We only care for Resized Images to reduce download time and disk space
                RegexStore.GuidRegex.Match(url)
                    .ToString()
                    .Split('/')
                    .Last()
                    .Contains("base_resized")
            ).ToArray();
        var gifCollection = RegexStore.ImageGifRegex.Matches(html)
            .Select(match => match.Value)
            .ToArray();
        var downloadCollection = hasValidCookie
            ? RegexStore.DownloadRegex.Matches(html)
                .Select(match => match.Value)
                .ToArray()
            : [];
        var ordersCollection = hasValidCookie
            ? RegexStore.OrdersRegex.Matches(html)
                .Select(match => match.Value)
                .ToArray()
            : [];

        // Thread safe container for collecting download urls
        var downloadBag = new ConcurrentBag<string>(downloadCollection);

        #endregion

        #region Parse Booth Order Pages

        try
        {
            if (ordersCollection.Length > 0)
            {
                await Task.WhenAll(ordersCollection.Select(url => Task.Run(async () =>
                {
                    var httpClient = client.MakeHttpClient();
                    Console.WriteLine("Building download collection for url: {0}", url);
                    var orderResponse = await httpClient.GetAsync(url, cancellationToken);
                    var orderHtml = await orderResponse.Content.ReadAsStringAsync(cancellationToken);

                    // Class separates the next item in the order list
                    var splitByItem = orderHtml.Split("\"u-d-flex\"");

                    foreach (var itemHtml in splitByItem)
                    {
                        var itemMatch = RegexStore.ItemRegex.Match(itemHtml);

                        if (itemMatch.Groups[1].Value != boothId) continue;
                        foreach (var downloadUrl in RegexStore.DownloadRegex.Matches(itemHtml)
                                     .Select(match => match.Value))
                        {
                            downloadBag.Add(downloadUrl);
                        }
                    }

                    Console.WriteLine("Finished building download collection for url: {0}", url);
                })));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            StringBuilder sb = new();
            sb.Append("Exception occured in order downloader.");
            sb.Append("Dumping orders collection: " + ordersCollection);
            sb.Append("Dumping orders urls downloadBag: " + downloadBag);
            Console.WriteLine(sb);
            throw new BoothClient.DownloadFailedException();
        }

        #endregion

        #region Download Processing
        var totalCount = imageCollection.Length + gifCollection.Length + downloadBag.Count;

        var options = new ProgressBarOptions
        {
            BackgroundColor = ConsoleColor.White,
            ForegroundColor = ConsoleColor.Yellow,
            ProgressCharacter = '─',
            CollapseWhenFinished = false
        };
        var parentOptions = new ProgressBarOptions
        {
            BackgroundColor = ConsoleColor.White,
            ForegroundColor = ConsoleColor.Blue,
            ProgressCharacter = '─',
            CollapseWhenFinished = false
        };
        var childOptions = new ProgressBarOptions
        {
            BackgroundColor = ConsoleColor.White,
            ForegroundColor = ConsoleColor.Green,
            ProgressCharacter = '─',
            CollapseWhenFinished = true
        };

        var progressBar = new ProgressBar(totalCount, "Overall Progress", options);
        ConcurrentBag<string> entryDirFiles = [];
        ConcurrentBag<string> binaryDirFiles = [];

        var imageTaskMessage = "ImageTasks";
        int remainingImageDownloads = imageCollection.Length;
        var imageTaskBar = progressBar.Spawn(imageCollection.Length, imageTaskMessage, parentOptions);
        var imageTasks = imageCollection.Select(url => Task.Run(async () =>
        {
            var filename = RegexStore.GuidRegex.Match(url).ToString().Split('/').Last();

            string uniqueFilename;
            lock (entryDirFiles)
            {
                uniqueFilename = Utils.GetUniqueFilename(binaryDir.ToString(), filename, binaryDirFiles);
                entryDirFiles.Add(uniqueFilename);
            }

            var child = imageTaskBar.Spawn(10000, uniqueFilename, childOptions);
            var childProgress = new ChildProgressBarProgress(child);

            await Utils.DownloadFileAsync(url, Path.Combine(entryDir.ToString(), uniqueFilename), childProgress, cancellationToken);

            imageTaskBar.Tick();
            progressBar.Tick();

            Interlocked.Decrement(ref remainingImageDownloads);
            imageTaskBar.Message = $"ImageTasks ({remainingImageDownloads} remaining)";
        })).ToArray();

        var gifTaskMessage = "GifTasks";
        int remainingGifDownloads = gifCollection.Length;
        var gifTaskBar = progressBar.Spawn(gifCollection.Length, gifTaskMessage, parentOptions);
        var gifTasks = gifCollection.Select(url => Task.Run(async () =>
        {
            var filename = RegexStore.GuidRegex.Match(url).ToString().Split('/').Last();

            string uniqueFilename;
            lock (entryDirFiles)
            {
                uniqueFilename = Utils.GetUniqueFilename(binaryDir.ToString(), filename, binaryDirFiles);
                entryDirFiles.Add(uniqueFilename);
            }

            var child = gifTaskBar.Spawn(10000, uniqueFilename, childOptions);
            var childProgress = new ChildProgressBarProgress(child);

            await Utils.DownloadFileAsync(url, Path.Combine(entryDir.ToString(), uniqueFilename), childProgress, cancellationToken);

            gifTaskBar.Tick();
            progressBar.Tick();

            Interlocked.Decrement(ref remainingGifDownloads);
            gifTaskBar.Message = $"GifTasks ({remainingGifDownloads} remaining)";
        })).ToArray();


        var downloadTaskMessage = "DownloadTasks";
        int remainingDownloads = downloadBag.Count;
        var downloadTaskBar = progressBar.Spawn(downloadBag.Count, downloadTaskMessage, parentOptions);
        var downloadTasks = downloadBag.Select(url => Task.Run(async () =>
        {
            var httpClient = client.MakeHttpClient();
            var resp = await httpClient.GetAsync(url, cancellationToken);
            var redirectUrl = resp.Headers.Location!.ToString();
            var filename = RegexStore.DownloadNameRegex.Match(redirectUrl).Groups[1].Value;

            string uniqueFilename;
            lock (binaryDirFiles)
            {
                uniqueFilename = Utils.GetUniqueFilename(binaryDir.ToString(), filename, binaryDirFiles);
                binaryDirFiles.Add(uniqueFilename);
            }

            var success = false;
            var retryCount = 0;
            var child = downloadTaskBar.Spawn(10000, uniqueFilename, childOptions);
            var childProgress = new ChildProgressBarProgress(child);
            while (!success && retryCount < maxRetries)
            {
                try
                {
                    child.Message = uniqueFilename;
                    await Utils.DownloadFileAsync(redirectUrl, Path.Combine(binaryDir.ToString(), uniqueFilename), childProgress, cancellationToken);
                    Interlocked.Decrement(ref remainingDownloads);
                    downloadTaskBar.Message = $"DownloadTasks ({remainingDownloads} remaining)";
                    success = true;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    child.Message = $"Failed to download {url}. Retry attempt {retryCount}/{maxRetries}. Error: {ex.Message}";
                    await Task.Delay(5000, cancellationToken);
                }
            }
            if (retryCount < maxRetries)
            {
                success = false;
            }

            downloadTaskBar.Tick();
            progressBar.Tick();

            if (success) return;

            Console.ForegroundColor = ConsoleColor.Red;
            child.Message = $"Failed to download {url} after {maxRetries} attempts.";
            Console.ResetColor();
        })).ToArray();

        if (imageTasks.Length == 0)
        {
            imageTaskBar.Tick();
            imageTaskBar.WriteLine("No images found skipping downloader.");
        }

        if (gifTasks.Length == 0)
        {
            gifTaskBar.Tick();
            gifTaskBar.WriteLine("No gifs found skipping downloader.");
        }

        if (downloadTasks.Length == 0)
        {
            downloadTaskBar.Tick();
            downloadTaskBar.WriteLine("No downloads found skipping downloader.");
        }

        var allTasks = imageTasks.Concat(gifTasks).Concat(downloadTasks).ToArray();
        await Task.WhenAll(allTasks);

        #endregion

        #region Compression

        if (config.Config.AutoZip)
        {

            progressBar.Dispose();
            if (File.Exists(entryDir + ".zip"))
            {
                Console.WriteLine("File already exists. Deleting...");
                File.Delete(entryDir + ".zip");
            }

            Console.WriteLine("Zipping!");
            ZipFile.CreateFromDirectory(entryDir.ToString(), entryDir + ".zip");
            Console.WriteLine("Zipped!");

            Directory.Delete(entryDir.ToString(), true);
        }

        #endregion

        #region Exit Successfully

        Console.WriteLine("Done!");

        if (idFromArgument && config.Config.AutoZip)
        {
            // used for standard output redirection for path to zip file with another process
            Console.WriteLine("ENVFilePATH: " + entryDir + ".zip");
        }

        #endregion
    }
}