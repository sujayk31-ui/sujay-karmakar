using DoctorApp.Data;
using DoctorApp.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

namespace DoctorApp.Tests;

public class CompanyCatalogTests
{
    [Fact]
    public async Task CompanySearchReturnsBrandsCompositionAndOnlyPrintedPrices()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"vaidya-catalog-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        await using var db = new AppDbContext(options);
        await DbSeeder.SeedAsync(db, new TestEnv(FindAppRoot()));
        var search = new ClinicSearch(db);

        var sun = await search.RunAsync(new SearchRequest { Q = "Sun" }, null);
        Assert.Equal("company", sun.Mode);
        var pantocid = sun.Companies.SelectMany(c => c.Products).Single(p => p.Name == "PANTOCID 40 MG TABLET 15");
        Assert.Contains("PANTOPRAZOLE", pantocid.Composition, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(195.94m, pantocid.MrpInr);
        var pantocid80 = sun.Companies.SelectMany(c => c.Products).Single(p => p.Name == "PANTOCID 80 MG TABLET 10");
        Assert.Null(pantocid80.MrpInr);

        var mankind = await search.RunAsync(new SearchRequest { Q = "Mankind" }, null);
        var mox = mankind.Companies.SelectMany(c => c.Products).First(p => p.Name == "Moxikind-CV");
        Assert.Contains("Amoxicillin", mox.Composition, StringComparison.OrdinalIgnoreCase);
        Assert.Null(mox.MrpInr);
        Assert.Contains(mankind.Companies.SelectMany(c => c.Products), p => p.Name == "Zady");

        var cipla = await search.RunAsync(new SearchRequest { Q = "Cipla" }, null);
        Assert.Contains(cipla.Companies.SelectMany(c => c.Products), p => p.Name == "DOMCET");
        Assert.All(cipla.Companies.SelectMany(c => c.Products), p => Assert.Null(p.MrpInr));

        var ipca = await search.RunAsync(new SearchRequest { Company = "Ipca Laboratories Limited" }, null);
        var calchek = ipca.Companies.SelectMany(c => c.Products).Single(p => p.Name.StartsWith("Calchek 5 Mg"));
        Assert.Contains("Amlodipine", calchek.Composition, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(26.67m, calchek.MrpInr);

        var alkem = await search.RunAsync(new SearchRequest { Q = "Alkem" }, null);
        Assert.Contains(alkem.Companies.SelectMany(c => c.Products), p => p.Composition.Contains("Ciprofloxacin", StringComparison.OrdinalIgnoreCase));
        Assert.All(alkem.Companies.SelectMany(c => c.Products), p => Assert.Null(p.MrpInr));

        var torrent = await search.RunAsync(new SearchRequest { Q = "Torrent" }, null);
        Assert.Contains(torrent.Companies.SelectMany(c => c.Products), p => p.Name.Contains("Ampoxin", StringComparison.OrdinalIgnoreCase));

        var fever = await search.RunAsync(new SearchRequest { Q = "fever" }, null);
        Assert.Equal("disease", fever.Mode);
        Assert.DoesNotContain(fever.Products, p => p.Name.Contains("chloroquine", StringComparison.OrdinalIgnoreCase) && !p.Name.Contains("hydroxy", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindAppRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var app = Path.Combine(dir.FullName, "DoctorApp");
            if (File.Exists(Path.Combine(app, "Data", "Seed", "catalog.json")))
                return app;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("DoctorApp seed catalog was not found.");
    }

    private sealed class TestEnv : IWebHostEnvironment
    {
        public TestEnv(string root)
        {
            ContentRootPath = root;
            WebRootPath = root;
        }

        public string ApplicationName { get; set; } = "DoctorApp";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
