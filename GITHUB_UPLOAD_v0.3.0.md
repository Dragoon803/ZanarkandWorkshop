# GitHub update checklist — v0.3.0

## Before updating the page

- Confirm the working tree contains only intended v0.3.0 source and documentation changes.
- Build the solution in Release configuration.
- Run the offline smoke-test suite.
- Confirm the README screenshots render in the expected order.
- Commit the final v0.3.0 source and push it to `main`.
- Confirm the GitHub Actions Build workflow passes.

## Optional draft release

Only create a release when the downloadable v0.3.0 package is ready:

```powershell
git tag -a v0.3.0 -m "Zanarkand Workshop v0.3.0"
git push origin v0.3.0
```

The tag workflow creates a draft release containing:

- `ZanarkandWorkshop-v0.3.0-win-x64.zip`
- `ZanarkandWorkshop-v0.3.0-win-x64.zip.sha256`
- Notes from `RELEASE_NOTES_v0.3.0.md`

## Before publishing a release

- Download the workflow-built ZIP and verify it against the attached SHA-256 file.
- Extract it into a new folder and launch `ZanarkandWorkshop.exe`.
- Confirm the application displays v0.3.0.
- Open a master-file project and test the new editors.
- Confirm the release targets tag `v0.3.0`.
- Publish the draft only after final verification.
