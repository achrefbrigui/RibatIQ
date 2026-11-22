namespace AiSample.Services;

public sealed class ReportService(HttpClient http, string reportsRoot)
{
    public string GetExchangeRates(string currency) =>
        http.GetStringAsync($"https://rates.example.test/{currency}").Result;

    public async Task<string> DownloadAsync(string url, CancellationToken cancellationToken) =>
        await http.GetStringAsync(url);

    public string ReadReport(string fileName)
    {
        var path = Path.Combine(reportsRoot, fileName);
        return File.ReadAllText(path);
    }

    public string ReadHeader(string fileName)
    {
        var reader = new StreamReader(Path.Combine(reportsRoot, "headers", Path.GetFileName(fileName)));
        return reader.ReadLine() ?? string.Empty;
    }
}
