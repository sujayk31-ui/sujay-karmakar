using DoctorApp.Models;
using DoctorApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DoctorApp.Pages;

public class CompaniesModel : PageModel
{
    private readonly ClinicSearch _search;
    private readonly UserManager<ApplicationUser> _users;

    public CompaniesModel(ClinicSearch search, UserManager<ApplicationUser> users)
    {
        _search = search;
        _users = users;
    }

    public List<CompanyGroup> Companies { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var doctor = await _users.GetUserAsync(User);
        Companies = await _search.ListCompaniesAsync(doctor?.Id);
    }
}
