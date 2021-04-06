namespace FileDownloader {
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Serilog;

    public class Program {
        public static Task Main(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices(collection => {
                    collection.AddHostedService<FileDownloadService>();
                    collection.AddHttpClient();
                })
                .ConfigureLogging(builder => builder.AddSerilog())
                .RunConsoleAsync();
    }
}
