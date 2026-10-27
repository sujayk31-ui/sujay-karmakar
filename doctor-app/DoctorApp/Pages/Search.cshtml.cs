using DoctorApp.Models;
using DoctorApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DoctorApp.Pages;

public class SearchModel : PageModel
{
    private readonly ClinicSearch _search;
    private readonly UserManager<ApplicationUser> _users;

    public SearchModel(ClinicSearch search, UserManager<ApplicationUser> users)
    {
        _search = search;
        _users = users;
    }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sub { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Company { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Antibiotic { get; set; }

    public SearchView Result { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var doctor = await _users.GetUserAsync(User);
        Result = await _search.RunAsync(new SearchRequest
        {
            Q = Q,
            Sub = Sub,
            Category = Category,
            Company = Company,
            Antibiotic = Antibiotic
        }, doctor?.Id);
    }
}
