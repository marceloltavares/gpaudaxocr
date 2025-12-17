using BankCheckOCR.Models;
using Google.Cloud.Vision.V1;
using Google.Apis.Auth.OAuth2;
using Grpc.Auth;
using System.Text.RegularExpressions;

namespace BankCheckOCR.Services
{
    public class VisionService : IVisionService
    {
        private readonly ILogger<VisionService> _logger;

        public VisionService(ILogger<VisionService> logger)
        {
            _logger = logger;
        }

        public async Task<CheckResult> AnalyzeCheckAsync(Stream imageStream)
        {
            var result = new CheckResult();

            try
            {
                // Initialize the client.
                // We check if GOOGLE_CREDENTIALS_JSON is set, if so, we create credentials from it.
                // Otherwise we fallback to the default which looks for GOOGLE_APPLICATION_CREDENTIALS file path.

                ImageAnnotatorClient client;
                var jsonCredentials = Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS_JSON");

                if (!string.IsNullOrEmpty(jsonCredentials))
                {
                    var credential = GoogleCredential.FromJson(jsonCredentials);
                    var clientBuilder = new ImageAnnotatorClientBuilder
                    {
                        ChannelCredentials = credential.ToChannelCredentials()
                    };
                    client = await clientBuilder.BuildAsync();
                }
                else
                {
                    client = await ImageAnnotatorClient.CreateAsync();
                }

                // Convert stream to Google Image
                var image = await Image.FromStreamAsync(imageStream);

                // Perform text detection
                var response = await client.DetectTextAsync(image);

                if (response == null || response.Count == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "No text detected.";
                    return result;
                }

                // The first annotation is the full text
                var fullText = response[0].Description;
                result.RawText = fullText;

                // Parse the text
                ParseCheckData(result, fullText);

                result.Success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing check image.");
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private void ParseCheckData(CheckResult result, string text)
        {
            // Disclaimer: This is a heuristic parser and may not work for all check layouts.

            // 1. Try to find CMC7
            // Pattern: 8 digits - 10 digits - 12 digits (roughly)
            // Sometimes OCR adds spaces or reads the special CMC7 font symbols as < or >
            var cmc7Regex = new Regex(@"\d{8}\s*\d{10}\s*\d{12}");
            var cmc7Match = cmc7Regex.Match(text.Replace("\n", "")); // Flatten text to search across lines if needed
            if (cmc7Match.Success)
            {
                result.CMC7 = cmc7Match.Value;

                // Attempt to extract bank code from CMC7 (first 3 digits usually)
                if (result.CMC7.Length >= 3)
                {
                    result.BankCode = result.CMC7.Substring(0, 3);
                }
            }

            // 2. Try to find Amount
            // Look for R$ followed by numbers
            var amountRegex = new Regex(@"R\$\s?([\d.,]+)");
            var amountMatch = amountRegex.Match(text);
            if (amountMatch.Success)
            {
                result.Amount = amountMatch.Groups[1].Value;
            }
            else
            {
                 // Fallback: look for patterns like #100,00# which is common on checks
                 var fallbackAmount = new Regex(@"#\s?([\d.,]+)\s?#");
                 var fbMatch = fallbackAmount.Match(text);
                 if (fbMatch.Success)
                 {
                     result.Amount = fbMatch.Groups[1].Value;
                 }
            }

            // 3. Try to find Date
            // Look for standard date formats dd/mm/yyyy
            var dateRegex = new Regex(@"\d{2}/\d{2}/\d{2,4}");
            var dateMatch = dateRegex.Match(text);
            if (dateMatch.Success)
            {
                result.Date = dateMatch.Value;
            }
            else
            {
                // Look for explicit month names
                string[] months = { "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho", "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro" };
                foreach (var month in months)
                {
                    if (text.Contains(month, StringComparison.OrdinalIgnoreCase))
                    {
                        // Try to grab the line containing the month
                        var lines = text.Split('\n');
                        foreach(var line in lines)
                        {
                            if (line.Contains(month, StringComparison.OrdinalIgnoreCase))
                            {
                                result.Date = line.Trim(); // Return the whole date line (e.g., "São Paulo, 10 de Janeiro de 2023")
                                break;
                            }
                        }
                        if (result.Date != null) break;
                    }
                }
            }
        }
    }
}
