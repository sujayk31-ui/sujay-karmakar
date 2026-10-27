using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace DoctorApp.Tests;

public class ClinicFlowTests : IClassFixture<ClinicFlowTests.ClinicApp>
{
    private readonly HttpClient _client;

    public ClinicFlowTests(ClinicApp app) => _client = app.Client;

    [Fact]
    public async Task DoctorCanRegisterSearchAndPrescribe()
    {
        var login = await _client.GetStringAsync("/Account/Login");
        MustContain(login, "User ID and password");
        MustContain(login, "6-digit PIN");
        MustContain(login, "platform authenticator");

        var loginOptions = await PostJson("/webauthn/login-options", Token(login));
        MustContain(loginOptions, "challenge");

        var registerPage = await _client.GetStringAsync("/Account/Register");
        var registered = await PostForm("/Account/Register", Fields(Token(registerPage),
            ("Input.FullName", "Asha Rao"),
            ("Input.Degree", "MBBS"),
            ("Input.Phone", "9988776655"),
            ("Input.UserId", "asha.rao"),
            ("Input.Password", "clinicpass1"),
            ("Input.ConfirmPassword", "clinicpass1"),
            ("Input.Pin", "246810"),
            ("Input.ConfirmPin", "246810")));
        MustContain(registered, "Fingerprint");

        var home = await _client.GetStringAsync("/");
        MustContain(home, "Dr. Asha Rao");
        MustContain(home, "tabbar");
        MustContain(home, "sidebar");
        MustContain(home, "name=\"viewport\"");
        Assert.DoesNotContain("bootstrap.min.css", home);
        var css = await _client.GetStringAsync("/css/site.css");
        MustContain(css, "min-width: 960px");

        var diseases = await _client.GetStringAsync("/Diseases");
        MustContain(diseases, "Malaria");
        MustContain(diseases, "Dengue");
        MustContain(diseases, "Fever");
        MustContain(diseases, "Infectious or parasitic diseases");
        MustContain(diseases, "Symptoms and signs");

        var malaria = await _client.GetStringAsync("/Search?q=malaria");
        MustContain(malaria, "Infectious or parasitic diseases");
        MustContain(malaria, "class=\"med\"");
        var dengue = await _client.GetStringAsync("/Search?q=dengue");
        MustContain(dengue, ">Dengue<");
        MustContain(dengue, "Infectious or parasitic diseases");
        MustContain(dengue, "class=\"med\"");
        var fever = await _client.GetStringAsync("/Search?q=fever");
        MustContain(fever, "Symptoms and signs");
        MustContain(fever, "class=\"med\"");

        var sun = await _client.GetStringAsync("/Search?company=Sun%20Pharmaceutical%20Industries%20Ltd.&q=PANTOCID%2040");
        MustContain(sun, "PANTOCID 40 MG TABLET 15");
        MustContain(sun, "195.94");
        MustContain(sun, "PANTOPRAZOLE");
        var sun80 = await _client.GetStringAsync("/Search?company=Sun%20Pharmaceutical%20Industries%20Ltd.&q=PANTOCID%2080%20MG%20TABLET%2010");
        Assert.Matches(new Regex("PANTOCID 80 MG TABLET 10[\\s\\S]{0,600}No MRP in source"), sun80);

        var mankind = await _client.GetStringAsync("/Search?q=Moxikind-CV");
        Assert.Matches(new Regex("Moxikind-CV[\\s\\S]{0,250}No MRP in source[\\s\\S]{0,250}Amoxicillin", RegexOptions.IgnoreCase), mankind);

        var companies = await _client.GetStringAsync("/Companies");
        MustContain(companies, "Sun Pharmaceutical Industries Ltd.");
        MustContain(companies, "Mankind Pharma Limited");

        var medsPage = await _client.GetStringAsync("/Medicines");
        var added = await PostForm("/Medicines", Fields(Token(medsPage),
            ("Name", "Zz Clinic Drops"),
            ("Composition", "Paracetamol 125 mg per 5 ml"),
            ("DoseForm", "Syrup")));
        MustContain(added, "Zz Clinic Drops");
        var mine = await _client.GetStringAsync("/Search?q=Zz%20Clinic");
        MustContain(mine, "Paracetamol 125 mg per 5 ml");

        await PostForm("/Account/Logout", Fields(Token(await _client.GetStringAsync("/"))));
        var registerB = await _client.GetStringAsync("/Account/Register");
        await PostForm("/Account/Register", Fields(Token(registerB),
            ("Input.FullName", "Bina Sen"),
            ("Input.Degree", "MD"),
            ("Input.Phone", "9876501234"),
            ("Input.UserId", "bina.sen"),
            ("Input.Password", "clinicpass2"),
            ("Input.ConfirmPassword", "clinicpass2"),
            ("Input.Pin", "135790"),
            ("Input.ConfirmPin", "135790")));
        var hidden = await _client.GetStringAsync("/Search?q=Zz%20Clinic");
        Assert.DoesNotContain("Zz Clinic Drops", hidden);

        await PostForm("/Account/Logout", Fields(Token(await _client.GetStringAsync("/"))));
        var pinPage = await _client.GetStringAsync("/Account/Login");
        var pinHome = await PostForm("/Account/Login?handler=Pin", Fields(Token(pinPage), ("Pin", "246810"), ("ReturnUrl", "/")));
        MustContain(pinHome, "Dr. Asha Rao");

        await PostForm("/Account/Logout", Fields(Token(pinHome)));
        var passwordPage = await _client.GetStringAsync("/Account/Login");
        var passwordHome = await PostForm("/Account/Login?handler=Password", Fields(Token(passwordPage),
            ("UserId", "bina.sen"),
            ("Password", "clinicpass2"),
            ("ReturnUrl", "/")));
        MustContain(passwordHome, "Dr. Bina Sen");

        await PostForm("/Account/Logout", Fields(Token(passwordHome)));
        var ashaLogin = await _client.GetStringAsync("/Account/Login");
        await PostForm("/Account/Login?handler=Password", Fields(Token(ashaLogin),
            ("UserId", "asha.rao"),
            ("Password", "clinicpass1"),
            ("ReturnUrl", "/")));

        var security = await _client.GetStringAsync("/Account/Security");
        var enroll = await PostJson("/webauthn/register-options", Token(security));
        MustContain(enroll, "platform");

        var suggest = await _client.GetStringAsync("/Prescriptions/New?handler=Suggest&q=Calchek");
        using var doc = JsonDocument.Parse(suggest);
        Assert.True(doc.RootElement.GetArrayLength() > 0, Snip(suggest));
        var medicineId = doc.RootElement[0].GetProperty("id").GetInt32().ToString();
        var medicineName = doc.RootElement[0].GetProperty("name").GetString() ?? "";

        var rxPage = await _client.GetStringAsync("/Prescriptions/New");
        var testMatch = Regex.Match(rxPage, "name=\"TestIds\" value=\"(\\d+)\"[\\s\\S]*?<span>([^<]+)</span>");
        Assert.True(testMatch.Success, Snip(rxPage));
        var saved = await PostForm("/Prescriptions/New", new[]
        {
            Pair("__RequestVerificationToken", Token(rxPage)),
            Pair("PatientName", "Mini Das"),
            Pair("PatientAge", "8 months"),
            Pair("WeightKg", "12"),
            Pair("BloodPressure", "110/70 sitting"),
            Pair("PatientPhone", "9876543210"),
            Pair("Diagnosis", "Viral fever"),
            Pair("Lines[0].MedicineId", medicineId),
            Pair("Lines[0].MedicineName", medicineName),
            Pair("Lines[0].Dosage", "2.5 ml twice a day"),
            Pair("Lines[0].Duration", "3 days"),
            Pair("Lines[0].Instructions", "After food"),
            Pair("Lines[1].MedicineName", "Zz Clinic Drops"),
            Pair("Lines[1].Composition", "Paracetamol 125 mg per 5 ml"),
            Pair("Lines[1].Dosage", "5 ml at night"),
            Pair("TestIds", testMatch.Groups[1].Value),
            Pair("CustomTests", "Morning smear")
        });

        MustContain(saved, "Dr. Asha Rao");
        MustContain(saved, "MBBS");
        MustContain(saved, "8 months");
        MustContain(saved, "12 kg");
        MustContain(saved, "110/70 sitting");
        MustContain(saved, medicineName);
        MustContain(saved, "Zz Clinic Drops");
        MustContain(saved, "Morning smear");
        MustContain(saved, testMatch.Groups[2].Value);
        MustContain(saved, "wa.me/919876543210");
        MustContain(saved, "99887 76655");

        var historyLink = Regex.Match(saved, "href=\"(/Patients/Details/\\d+)\"");
        Assert.True(historyLink.Success, Snip(saved));
        var history = await _client.GetStringAsync(historyLink.Groups[1].Value);
        MustContain(history, "Viral fever");
        MustContain(history, medicineName);
        MustContain(history, "Morning smear");
        MustContain(history, "8 months");
        MustContain(history, "110/70 sitting");
    }

    private async Task<string> PostForm(string url, IEnumerable<KeyValuePair<string, string>> fields)
    {
        var response = await _client.PostAsync(url, new FormUrlEncodedContent(fields));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{url} {(int)response.StatusCode} {Snip(body)}");
        return body;
    }

    private async Task<string> PostJson(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("RequestVerificationToken", token);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{url} {(int)response.StatusCode} {Snip(body)}");
        return body;
    }

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"request-verification-token\" content=\"([^\"]+)\"");
        Assert.True(match.Success, Snip(html));
        return match.Groups[1].Value;
    }

    private static IEnumerable<KeyValuePair<string, string>> Fields(string token, params (string Key, string Value)[] extra)
    {
        yield return Pair("__RequestVerificationToken", token);
        foreach (var item in extra)
            yield return Pair(item.Key, item.Value);
    }

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);

    private static void MustContain(string html, string expected) =>
        Assert.True(html.Contains(expected), $"Missing '{expected}'. {Snip(html)}");

    private static string Snip(string html) => html[..Math.Min(700, html.Length)];

    public sealed class ClinicApp : IDisposable
    {
        private readonly string _db = Path.Combine(Path.GetTempPath(), $"vaidya-ui-{Guid.NewGuid():N}.db");
        private readonly WebApplicationFactory<Program> _factory;

        public ClinicApp()
        {
            var db = _db;
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Default", $"Data Source={db};Pooling=False");
                builder.ConfigureLogging(logging => logging.ClearProviders());
            });
            Client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = true,
                HandleCookies = true
            });
            Client.Timeout = TimeSpan.FromMinutes(5);
        }

        public HttpClient Client { get; }

        public void Dispose()
        {
            Client.Dispose();
            _factory.Dispose();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try
                {
                    var path = _db + suffix;
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch
                {
                    // The temp database is outside the repo.
                }
            }
        }
    }
}
