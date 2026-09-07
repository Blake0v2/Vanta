# Releasing Vanta

1. Update `<Version>` in `src/Vanta.App/Vanta.App.csproj`.
2. Build and exercise the release locally.
3. Commit the version change and create a matching tag, for example `v0.1.0`.
4. Push the branch and tag.
5. Verify the GitHub Actions build and download the MSI from the draft release.
6. Confirm the signatures with `Get-AuthenticodeSignature` and compare the SHA-256 hash.
7. Test install, upgrade, launch, and uninstall in a clean Windows 11 virtual machine.

Do not publish the MSI as an official download until the signing secrets are configured. Unsigned software from a new publisher is more likely to show SmartScreen warnings even when the program is clean.
