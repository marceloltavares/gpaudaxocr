using BankCheckOCR.Models;
using Google.Cloud.Vision.V1;
using Google.Apis.Auth.OAuth2;
using Grpc.Auth;
using System.Text.RegularExpressions;
using System.Globalization; // Adicionado para tratar moeda PT-BR

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
                // --- CONFIGURAÇÃO DA CREDENCIAL VIA ARQUIVO ---
                string caminhoDoArquivoJson = @"C:\coral-bebop-428813-g3-b4507c2d39f9.json";

                ImageAnnotatorClient client;

                if (System.IO.File.Exists(caminhoDoArquivoJson))
                {
                    var credential = GoogleCredential.FromFile(caminhoDoArquivoJson);

                    var clientBuilder = new ImageAnnotatorClientBuilder
                    {
                        ChannelCredentials = credential.ToChannelCredentials()
                    };

                    client = await clientBuilder.BuildAsync();
                }
                else
                {
                    throw new System.IO.FileNotFoundException($"O arquivo de credencial não foi encontrado no caminho: {caminhoDoArquivoJson}");
                }

                var image = await Image.FromStreamAsync(imageStream);

                var response = await client.DetectTextAsync(image);

                if (response == null || response.Count == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "No text detected.";
                    return result;
                }

                var fullText = response[0].Description;
                result.RawText = fullText;

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
            // 1. CMC7
            var cmc7Regex = new Regex(@"\d{8}\s*\d{10}\s*\d{12}");
            var cmc7Match = cmc7Regex.Match(text.Replace("\n", ""));
            if (cmc7Match.Success)
            {
                result.CMC7 = cmc7Match.Value;
                if (result.CMC7.Length >= 3)
                {
                    result.BankCode = result.CMC7.Substring(0, 3);
                }
            }

            // 2. Amount (LÓGICA NOVA E MELHORADA)
            var moneyRegex = new Regex(@"\b\d{1,3}(?:\.\d{3})*,\d{2}\b");
            var matches = moneyRegex.Matches(text);

            decimal bestAmount = 0;
            bool foundStrict = false;

            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    string cleanValue = match.Value.Replace(".", "");

                    if (decimal.TryParse(cleanValue, NumberStyles.Currency, new CultureInfo("pt-BR"), out decimal currentAmount))
                    {
                        if (currentAmount > bestAmount)
                        {
                            bestAmount = currentAmount;
                            result.Amount = match.Value;
                            foundStrict = true;
                        }
                    }
                }
            }

            if (!foundStrict)
            {
                var looseRegex = new Regex(@"(\d[\d\.\s]*,\s*\d{2})");
                var looseMatch = looseRegex.Match(text);

                if (looseMatch.Success)
                {
                    string raw = Regex.Replace(looseMatch.Groups[1].Value, @"[^\d,]", "");

                    if (decimal.TryParse(raw, NumberStyles.Currency, new CultureInfo("pt-BR"), out decimal looseAmount))
                    {
                        result.Amount = looseAmount.ToString("N2", new CultureInfo("pt-BR"));
                    }
                }
            }

            // 3. Try to find Date (Mantido original)
            var dateRegex = new Regex(@"\d{2}/\d{2}/\d{2,4}");
            var dateMatch = dateRegex.Match(text);
            if (dateMatch.Success)
            {
                result.Date = dateMatch.Value;
            }
            else
            {
                string[] months = { "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho", "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro" };
                foreach (var month in months)
                {
                    if (text.Contains(month, StringComparison.OrdinalIgnoreCase))
                    {
                        var lines = text.Split('\n');
                        foreach (var line in lines)
                        {
                            if (line.Contains(month, StringComparison.OrdinalIgnoreCase))
                            {
                                result.Date = line.Trim();
                                break;
                            }
                        }
                        if (result.Date != null) break;
                    }
                }
            }

            // 4. Try to find CPF or CNPJ
            var labeledDocRegex = new Regex(@"(?:CNPJ|CPF)[\s:.]*([\d./-]+)", RegexOptions.IgnoreCase);
            var labeledMatch = labeledDocRegex.Match(text);

            bool docFound = false;

            if (labeledMatch.Success)
            {
                string rawDoc = labeledMatch.Groups[1].Value;
                if (IsValidCpf(rawDoc) || IsValidCnpj(rawDoc))
                {
                    result.IssuerDocument = rawDoc; 
                    docFound = true;
                }
            }

            if (!docFound)
            {
                var cnpjMaskRegex = new Regex(@"\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}");
                var cnpjMatches = cnpjMaskRegex.Matches(text);
                foreach (Match m in cnpjMatches)
                {
                    if (IsValidCnpj(m.Value))
                    {
                        result.IssuerDocument = m.Value;
                        docFound = true;
                        break;
                    }
                }
            }

            if (!docFound)
            {
                var cpfMaskRegex = new Regex(@"\d{3}\.\d{3}\.\d{3}-\d{2}");
                var cpfMatches = cpfMaskRegex.Matches(text);
                foreach (Match m in cpfMatches)
                {
                    if (IsValidCpf(m.Value))
                    {
                        result.IssuerDocument = m.Value;
                        docFound = true;
                        break;
                    }
                }
            }
        }

        private bool IsValidCnpj(string cnpj)
        {
            int[] multiplicador1 = new int[12] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
            int[] multiplicador2 = new int[13] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

            cnpj = cnpj.Trim().Replace(".", "").Replace("-", "").Replace("/", "");

            if (cnpj.Length != 14) return false;

            if (new string(cnpj[0], 14) == cnpj) return false;

            string tempCnpj = cnpj.Substring(0, 12);
            int soma = 0;

            for (int i = 0; i < 12; i++)
                soma += int.Parse(tempCnpj[i].ToString()) * multiplicador1[i];

            int resto = (soma % 11);
            if (resto < 2) resto = 0;
            else resto = 11 - resto;

            string digito = resto.ToString();
            tempCnpj = tempCnpj + digito;
            soma = 0;
            for (int i = 0; i < 13; i++)
                soma += int.Parse(tempCnpj[i].ToString()) * multiplicador2[i];

            resto = (soma % 11);
            if (resto < 2) resto = 0;
            else resto = 11 - resto;

            digito = digito + resto.ToString();
            return cnpj.EndsWith(digito);
        }

        private bool IsValidCpf(string cpf)
        {
            int[] multiplicador1 = new int[9] { 10, 9, 8, 7, 6, 5, 4, 3, 2 };
            int[] multiplicador2 = new int[10] { 11, 10, 9, 8, 7, 6, 5, 4, 3, 2 };

            cpf = cpf.Trim().Replace(".", "").Replace("-", "");

            if (cpf.Length != 11) return false;

            if (new string(cpf[0], 11) == cpf) return false;

            string tempCpf = cpf.Substring(0, 9);
            int soma = 0;

            for (int i = 0; i < 9; i++)
                soma += int.Parse(tempCpf[i].ToString()) * multiplicador1[i];

            int resto = soma % 11;
            if (resto < 2) resto = 0;
            else resto = 11 - resto;

            string digito = resto.ToString();
            tempCpf = tempCpf + digito;
            soma = 0;
            for (int i = 0; i < 10; i++)
                soma += int.Parse(tempCpf[i].ToString()) * multiplicador2[i];

            resto = soma % 11;
            if (resto < 2) resto = 0;
            else resto = 11 - resto;

            digito = digito + resto.ToString();
            return cpf.EndsWith(digito);
        }
    }
}