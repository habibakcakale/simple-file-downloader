using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FileDownloader
{
    public class FileDownloadService : IHostedService
    {
        private readonly ILogger<FileDownloadService> logger;
        private static readonly AsyncRetryPolicy Policy = CreatePolicy();
        private readonly IHostApplicationLifetime applicationLifetime;
        private readonly IHttpClientFactory httpClientFactory;

        private static readonly Regex PatternReplacer =
            new("\\{(?<pattern>[a-zA-Z0-9\\.\\(\\)-:]*?)\\}", RegexOptions.Compiled);

        public FileDownloadService(IHostApplicationLifetime applicationLifetime, IHttpClientFactory httpClientFactory,
            ILogger<FileDownloadService> logger)
        {
            this.applicationLifetime = applicationLifetime;
            this.httpClientFactory = httpClientFactory;
            this.logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var lines = await File.ReadAllTextAsync("url-list.yaml", Encoding.UTF8, cancellationToken);
            var serializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            var downloadModel = serializer.Deserialize<Model>(lines);
            var tasks = downloadModel.Urls.Select(async pair =>
            {
                try
                {
                    logger.LogInformation("Processing {Name}", pair.Key);
                    await Policy.ExecuteAsync(async () => await ProcessLine(downloadModel.DownloadPath, pair));
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Exception occured while processing {Name}, {Url}", pair.Key, pair.Value);
                    Console.WriteLine(e);
                }
            });
            await Task.WhenAll(tasks);
            applicationLifetime.StopApplication();
        }

        private async Task ProcessLine(string downloadPath, KeyValuePair<string, string> pair)
        {
            var (fileName, url) = pair;
            url = PatternReplacer.Replace(url, match =>
            {
                var exp = match.Groups["pattern"].Value.Split(":");
                var state = CSharpScript.RunAsync<object>(exp[0],
                        ScriptOptions.Default.AddReferences(typeof(DateTime).Assembly).AddImports("System"))
                    .GetAwaiter()
                    .GetResult();
                return exp.Length > 1
                    ? string.Format($"{{0:{exp[1]}}}", state.ReturnValue)
                    : state.ReturnValue.ToString();
            });
            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = Timeout.InfiniteTimeSpan;

            using var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(url),
            };
            var responseMessage = await httpClient.SendAsync(request);
            var stream = await responseMessage.Content.ReadAsStreamAsync();
            var fileStream = File.OpenWrite(Path.Join(downloadPath, fileName));
            await stream.CopyToAsync(fileStream);
        }

        private static AsyncRetryPolicy CreatePolicy()
        {
            return Polly.Policy.Handle<TaskCanceledException>().Or<HttpRequestException>()
                .WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(i));
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}