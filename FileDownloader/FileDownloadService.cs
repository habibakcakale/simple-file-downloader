namespace FileDownloader {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.CodeAnalysis.CSharp.Scripting;
    using Microsoft.CodeAnalysis.Scripting;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Polly;
    using Polly.Retry;

    public class FileDownloadService : IHostedService {
        private readonly ILogger<FileDownloadService> logger;
        private static readonly AsyncRetryPolicy Policy = CreatePolicy();
        private readonly IHostApplicationLifetime applicationLifetime;
        private readonly IHttpClientFactory httpClientFactory;

        private static readonly Regex PatternReplacer = new("\\{(?<pattern>[a-zA-Z0-9\\.\\(\\)-:]*?)\\}", RegexOptions.Compiled);

        public FileDownloadService(IHostApplicationLifetime applicationLifetime, IHttpClientFactory httpClientFactory,
            ILogger<FileDownloadService> logger) {
            this.applicationLifetime = applicationLifetime;
            this.httpClientFactory = httpClientFactory;
            this.logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken) {
            var fileInfo = new FileInfo("url-list.json");
            if (!fileInfo.Exists) {
                logger.LogError("File not found :{FileName}", fileInfo.Name);
                return;
            }

            var downloadModel = await JsonSerializer.DeserializeAsync<Model>(fileInfo.OpenRead(), new JsonSerializerOptions {PropertyNamingPolicy = JsonNamingPolicy.CamelCase}, cancellationToken);
            Directory.CreateDirectory(downloadModel.DownloadPath);
            foreach (var pair in downloadModel.Urls) {
                try {
                    logger.LogInformation("Processing {Name}", pair.Key);
                    await Policy.ExecuteAsync(async (_, token) => await ProcessLine(downloadModel.DownloadPath, pair, token), new Context(), cancellationToken);
                } catch (Exception e) {
                    logger.LogError(e, "Exception occured while processing {Name}, {Url}", pair.Key, pair.Value);
                    Console.WriteLine(e);
                }
            }

            applicationLifetime.StopApplication();
        }

        private async Task ProcessLine(string downloadPath, KeyValuePair<string, string> pair, CancellationToken cancellationToken) {
            var (fileName, url) = pair;
            url = PatternReplacer.Replace(url, match => ReplacePattern(match, cancellationToken));
            
            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = Timeout.InfiniteTimeSpan;
            using var request = new HttpRequestMessage {
                Method = HttpMethod.Get,
                RequestUri = new Uri(url),
            };
            var responseMessage = await httpClient.SendAsync(request, cancellationToken);
            var stream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
            var fileStream = File.Create(Path.Join(downloadPath, fileName));
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        private static string ReplacePattern(Match match, CancellationToken cancellationToken) {
            var exp = match.Groups["pattern"].Value.Split(":");
            var state = CSharpScript.RunAsync<object>(exp[0],
                    ScriptOptions.Default.AddImports("System"), cancellationToken: cancellationToken)
                .GetAwaiter()
                .GetResult();
            return exp.Length > 1
                ? string.Format($"{{0:{exp[1]}}}", state.ReturnValue)
                : state.ReturnValue.ToString();
        }

        private static AsyncRetryPolicy CreatePolicy() {
            return Polly.Policy.Handle<HttpRequestException>()
                .WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(i));
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
