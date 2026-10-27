# Vaidya

A single ASP.NET Core Razor Pages app for one clinic. Accounts, prescriptions, and the medicine catalog live in a SQLite file. There is no separate API service.

## Run

From the repository root:

```bash
dotnet run --project doctor-app/DoctorApp
```

Or from `doctor-app`:

```bash
dotnet run --project DoctorApp
```

The development URL is `http://localhost:5080`. Register a doctor on the first visit. There is no demo account.

The database file is created at `doctor-app/DoctorApp/App_Data/clinic.db` the first time the app starts. The medicine, disease, and test catalogs are already in `DoctorApp/Data/Seed/catalog.json` and are copied in only when the product table is empty. Do not rebuild that file to start the app. If a schema change is made before the first successful start, delete `clinic.db` and start again.

Tests:

```bash
dotnet test doctor-app/DoctorApp.sln
```

## Layout

```
doctor-app/
  DoctorApp.sln
  DoctorApp/
    Program.cs                  Identity cookie, SQLite, WebAuthn session
    Pages/                      Mobile-first screens
    Controllers/WebAuthnController.cs
    Services/ClinicServices.cs  Search, PIN rules, WhatsApp text
    Models/ClinicModels.cs
    Data/                       EF Core context, seeder, catalog.json
    wwwroot/css/site.css        The only UI stylesheet
    wwwroot/js/                 Passkey ceremony and prescription picker
  DoctorApp.Tests/
  tools/                        Parsers that produced the seed. Not used at runtime.
```

## Sign-in

Registration stores the doctor’s name, degree, phone, user ID, password, and a unique 6-digit PIN. The PIN is hashed. Sign-in then offers:

1. User ID and password.
2. PIN alone.
3. Fingerprint, using WebAuthn with a platform authenticator, a discoverable credential, and user verification required. If the browser has no platform authenticator, the page says so and does not pretend the check succeeded.
