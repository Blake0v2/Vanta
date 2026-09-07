# Vanta

Vanta is a lightweight, native Windows auto clicker with a compact dark interface. It is written in C# using WPF and uses only Windows and .NET APIs—there are no advertisements, analytics, injected DLLs, drivers, or background services.

## Features

- Compact Home screen modeled on the supplied Vanta reference
- Global configurable hotkey with toggle and hold activation modes
- Clicks-per-millisecond, second, minute, or hour cadence
- Left, middle, and right mouse buttons
- Persistent per-user settings and always-on-top mode
- Fixed-size, non-maximizable window with working Home, Advanced, and Settings navigation
- MSI installer and portable ZIP release artifacts

## Build locally

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then run:

```powershell
dotnet restore Vanta.sln --configfile NuGet.Config
dotnet build Vanta.sln --configuration Release --no-restore
dotnet run --project src/Vanta.App/Vanta.App.csproj
```

## Releases

Push a version tag such as `v0.1.0` to run the Windows release workflow. It builds:

- `Vanta-0.1.0-x64.msi` — Windows Installer package with Start menu and uninstall integration
- `Vanta-0.1.0-win-x64.zip` — portable, self-contained Windows x64 build
- `SHA256SUMS.txt` — hashes users can verify after download

For a signed release, add these GitHub Actions repository secrets before pushing the tag:

- `CODE_SIGNING_PFX`: Base64-encoded Authenticode `.pfx` certificate
- `CODE_SIGNING_PASSWORD`: Password for that certificate

The workflow signs the application before packaging and signs the final MSI with an RFC 3161 timestamp.

## Windows reputation and antivirus guidance

No publisher can honestly guarantee that every security product will accept a brand-new executable immediately. Vanta's release setup is deliberately conventional and auditable: WPF/.NET, standard `SendInput`, a standard MSI, no packer, no obfuscation, no self-extracting launcher, no elevation at app runtime, and deterministic source builds.

For the lowest-friction public release:

1. Sign every tagged release with an OV or EV Authenticode certificate whose legal publisher name is stable.
2. Keep the same certificate across releases so Microsoft Defender SmartScreen can accumulate reputation.
3. Publish through GitHub Releases over HTTPS and include the generated SHA-256 checksums.
4. Submit the first signed build to the [Microsoft Store](https://developer.microsoft.com/microsoft-store/) or Microsoft Defender submission portal if it receives a false positive.
5. Never ship an unsigned installer as an official release.

The app must run at the same privilege level as the program it clicks. It intentionally requests normal user privileges; clicking an elevated administrator window would require launching Vanta as administrator too.

## Privacy

Vanta stores settings locally under `%LOCALAPPDATA%\Vanta`. The only network request is a user-initiated check of the public GitHub Releases API.
